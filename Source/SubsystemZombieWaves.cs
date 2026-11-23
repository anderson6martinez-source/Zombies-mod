using System.Collections.Generic;
using Engine;
using GameEntitySystem;
using TemplatesDatabase;

namespace Game
{
    // Oleadas nocturnas de zombis: una por noche, cada vez mas grande.
    public class SubsystemZombieWaves : Subsystem, IUpdateable
    {
        // ---- Ajustes ----
        const float WaveChance = 0.5f;    // probabilidad de oleada cada noche (0 a 1)
        const int BaseZombies = 4;        // zombis de la oleada 1 (por jugador)
        const int ExtraPerWave = 2;       // cuantos se suman en cada oleada nueva
        const int MaxPerPlayer = 40;      // tope por jugador
        const float SpawnInterval = 0.6f; // segundos entre zombis (evita lag)
        const int MinDistance = 28;
        const int MaxDistance = 42;
        const int NightSkyLight = 4;      // luz de cielo <= esto = noche
        const float GordoExplosionPressure = 1f; // fuerza de la explosion del gordo (sube el numero para mas dano)
        const string GordoName = "ZombieKGordo";
        const string SpitterName = "ZombieKEscupidor";
        const string ReviveName = "ZombieKRenacido";
        // escupidor
        const float SpitCooldown = 2.5f;   // segundos entre disparos
        const float SpitMinRange = 5f;
        const float SpitMaxRange = 18f;
        const float SpitSpeed = 22f;
        const float SpitDamage = 1.2f;     // dano de cada escupitajo
        const float SpitMaxAge = 6f;       // segundos maximos de vuelo
        // renacido
        const float ReviveDelay = 3f;      // segundos tirado antes de levantarse
        const float ReviveHealth = 0.4f;   // vida (0 a 1) al levantarse

        SubsystemTimeOfDay m_subsystemTimeOfDay;
        SubsystemSky m_subsystemSky;
        SubsystemPlayers m_subsystemPlayers;
        SubsystemTerrain m_subsystemTerrain;
        SubsystemExplosions m_subsystemExplosions;
        SubsystemProjectiles m_subsystemProjectiles;
        SubsystemCreatureSpawn m_subsystemCreatureSpawn;
        class Spit { public Projectile P; public ComponentCreature Owner; public Vector3 Prev; public float Age; }
        List<Spit> m_spits = new List<Spit>();
        class Tracked
        {
            public string Name;
            public Vector3 Pos;
            public bool SecondLife;
            public float Cooldown;
            public ComponentHealth Health;
            public ComponentBody Body;
            public ComponentChaseBehavior Chase;
            public ComponentCreature Creature;
        }
        Dictionary<Entity, Tracked> m_tracked = new Dictionary<Entity, Tracked>();
        List<Vector3> m_reviveQueue = new List<Vector3>();
        List<float> m_reviveTimes = new List<float>();
        float m_scanTimer;

        Game.Random m_random = new Game.Random();
        int m_lastWaveDay = -1;
        int m_firstDay = -1;
        int m_waveNumber;
        bool m_active;
        float m_timer;
        Dictionary<ComponentPlayer, int> m_pending = new Dictionary<ComponentPlayer, int>();

        public UpdateOrder UpdateOrder { get { return UpdateOrder.Default; } }

        public override void Load(ValuesDictionary valuesDictionary)
        {
            base.Load(valuesDictionary);
            m_subsystemTimeOfDay = Project.FindSubsystem<SubsystemTimeOfDay>(true);
            m_subsystemSky = Project.FindSubsystem<SubsystemSky>(true);
            m_subsystemPlayers = Project.FindSubsystem<SubsystemPlayers>(true);
            m_subsystemTerrain = Project.FindSubsystem<SubsystemTerrain>(true);
            m_subsystemExplosions = Project.FindSubsystem<SubsystemExplosions>(true);
            m_subsystemProjectiles = Project.FindSubsystem<SubsystemProjectiles>(true);
            m_subsystemCreatureSpawn = Project.FindSubsystem<SubsystemCreatureSpawn>(true);
            m_lastWaveDay = valuesDictionary.GetValue<int>("LastWaveDay", -1);
            m_firstDay = valuesDictionary.GetValue<int>("FirstDay", -1);
            m_waveNumber = valuesDictionary.GetValue<int>("WaveNumber", 0);
        }

        public override void Save(ValuesDictionary valuesDictionary)
        {
            base.Save(valuesDictionary);
            valuesDictionary.SetValue("LastWaveDay", m_lastWaveDay);
            valuesDictionary.SetValue("FirstDay", m_firstDay);
            valuesDictionary.SetValue("WaveNumber", m_waveNumber);
        }

        static bool IsSpecial(string n) { return n == GordoName || n == SpitterName || n == ReviveName; }

        void Track(Entity entity, bool secondLife)
        {
            m_tracked[entity] = new Tracked
            {
                Name = entity.ValuesDictionary.DatabaseObject.Name,
                SecondLife = secondLife,
                Cooldown = SpitCooldown,
                Health = entity.FindComponent<ComponentHealth>(true),
                Body = entity.FindComponent<ComponentBody>(true),
                Chase = entity.FindComponent<ComponentChaseBehavior>(false),
                Creature = entity.FindComponent<ComponentCreature>(true),
                Pos = entity.FindComponent<ComponentBody>(true).Position
            };
        }

        // Gordo explota, escupidor dispara, renacido se levanta una vez.
        void UpdateSpecials(float dt)
        {
            m_scanTimer -= dt;
            if (m_scanTimer <= 0f)
            {
                m_scanTimer = 0.25f;
                foreach (Entity entity in Project.Entities)
                {
                    if (!m_tracked.ContainsKey(entity) && IsSpecial(entity.ValuesDictionary.DatabaseObject.Name))
                        Track(entity, false);
                }
            }

            List<Entity> done = null;
            foreach (KeyValuePair<Entity, Tracked> kv in new List<KeyValuePair<Entity, Tracked>>(m_tracked))
            {
                Entity entity = kv.Key;
                Tracked t = kv.Value;
                bool alive = entity.IsAddedToProject;
                if (alive) t.Pos = t.Body.Position;

                if (t.Health.Health <= 0f)
                {
                    OnDeath(t);
                    if (done == null) done = new List<Entity>();
                    done.Add(entity);
                }
                else if (!alive)
                {
                    if (done == null) done = new List<Entity>();
                    done.Add(entity); // desaparecio sin morir (despawn)
                }
                else if (t.Name == SpitterName)
                {
                    UpdateSpitter(t, dt);
                }
            }
            if (done != null) foreach (Entity e in done) m_tracked.Remove(e);

            for (int i = m_reviveQueue.Count - 1; i >= 0; i--)
            {
                m_reviveTimes[i] -= dt;
                if (m_reviveTimes[i] > 0f) continue;
                Vector3 pos = m_reviveQueue[i];
                m_reviveQueue.RemoveAt(i);
                m_reviveTimes.RemoveAt(i);
                Entity zombie = DatabaseManager.CreateEntity(Project, ReviveName, true);
                zombie.FindComponent<ComponentBody>(true).Position = pos + new Vector3(0f, 0.2f, 0f);
                Project.AddEntity(zombie);
                zombie.FindComponent<ComponentHealth>(true).Health = ReviveHealth;
                Track(zombie, true); // ya uso su segunda vida
            }
        }

        void OnDeath(Tracked t)
        {
            Vector3 pos = t.Pos;
            if (t.Name == GordoName)
            {
                m_subsystemExplosions.AddExplosion((int)MathUtils.Floor(pos.X), (int)MathUtils.Floor(pos.Y + 1f), (int)MathUtils.Floor(pos.Z), GordoExplosionPressure, false, false);
            }
            else if (t.Name == ReviveName && !t.SecondLife)
            {
                m_reviveQueue.Add(pos);
                m_reviveTimes.Add(ReviveDelay);
            }
        }

        void UpdateSpitter(Tracked t, float dt)
        {
            t.Cooldown -= dt;
            if (t.Cooldown > 0f || t.Chase == null) return;
            ComponentCreature target = t.Chase.Target;
            if (target == null) return;

            Vector3 from = t.Body.Position + new Vector3(0f, 1.5f, 0f);
            Vector3 to = target.ComponentBody.Position + new Vector3(0f, 0.9f, 0f);
            Vector3 d = to - from;
            float dist = d.Length();
            if (dist < SpitMinRange || dist > SpitMaxRange) return;

            t.Cooldown = SpitCooldown;
            Vector3 velocity = d / dist * SpitSpeed + new Vector3(0f, dist * 0.12f, 0f);
            // bola de nieve: se destruye al impactar. El dano lo aplicamos nosotros (SpitDamage).
            int ball = Terrain.MakeBlockValue(SnowballBlock.Index, 0, 0);
            Projectile projectile = m_subsystemProjectiles.FireProjectile(ball, from, velocity, Vector3.Zero, t.Creature);
            if (projectile != null)
                m_spits.Add(new Spit { P = projectile, Owner = t.Creature, Prev = from });
        }

        static bool Inside(BoundingBox b, Vector3 p, float m)
        {
            return p.X >= b.Min.X - m && p.X <= b.Max.X + m && p.Y >= b.Min.Y - m && p.Y <= b.Max.Y + m && p.Z >= b.Min.Z - m && p.Z <= b.Max.Z + m;
        }

        bool HitSomething(Spit s, ComponentCreature c, Vector3 pos)
        {
            if (c == null || c == s.Owner || c.ComponentHealth.Health <= 0f) return false;
            if (c.Entity.ValuesDictionary.DatabaseObject.Name.StartsWith("ZombieK")) return false; // sin fuego amigo
            BoundingBox box = c.ComponentBody.BoundingBox;
            Vector3 mid = (pos + s.Prev) * 0.5f;
            if (!Inside(box, pos, 0.1f) && !Inside(box, mid, 0.1f)) return false;
            c.ComponentHealth.Injure(SpitDamage, s.Owner, false, "Escupido por un zombi");
            return true;
        }

        void UpdateSpits(float dt)
        {
            for (int i = m_spits.Count - 1; i >= 0; i--)
            {
                Spit s = m_spits[i];
                s.Age += dt;
                if (s.P.ToRemove || s.Age > SpitMaxAge) { m_spits.RemoveAt(i); continue; }
                Vector3 pos = s.P.Position;
                bool hit = false;
                foreach (ComponentPlayer player in m_subsystemPlayers.ComponentPlayers)
                    if (HitSomething(s, player.ComponentCreature, pos)) { hit = true; break; }
                if (!hit)
                    foreach (ComponentCreature creature in m_subsystemCreatureSpawn.Creatures)
                        if (HitSomething(s, creature, pos)) { hit = true; break; }
                if (hit) { s.P.ToRemove = true; m_spits.RemoveAt(i); }
                else s.Prev = pos;
            }
        }

        public void Update(float dt)
        {
            UpdateSpecials(dt);
            UpdateSpits(dt);
            int day = m_subsystemTimeOfDay.Day;
            if (m_firstDay < 0) m_firstDay = day;
            bool night = m_subsystemSky.SkyLightValue <= NightSkyLight;

            if (!m_active)
            {
                if (night && day != m_lastWaveDay && m_subsystemPlayers.ComponentPlayers.Count > 0)
                {
                    m_lastWaveDay = day; // se tira el dado una sola vez por noche
                    if (m_random.UniformFloat(0f, 1f) < WaveChance) StartWave(day);
                }
                return;
            }

            // la oleada termina al amanecer o cuando ya salieron todos
            if (!night) { EndWave(); return; }

            m_timer -= dt;
            if (m_timer > 0f) return;
            m_timer = SpawnInterval;

            bool any = false;
            foreach (ComponentPlayer player in new List<ComponentPlayer>(m_pending.Keys))
            {
                int left = m_pending[player];
                if (left <= 0) continue;
                any = true;
                if (player.ComponentHealth.Health <= 0f) { m_pending[player] = 0; continue; }
                if (TrySpawn(player)) m_pending[player] = left - 1;
            }
            if (!any) EndWave();
        }

        void StartWave(int day)
        {
            m_active = true;
            m_waveNumber++; // cuenta solo las oleadas que de verdad ocurren
            m_timer = 3f;
            m_pending.Clear();
            int count = MathUtils.Min(BaseZombies + ExtraPerWave * (m_waveNumber - 1), MaxPerPlayer);
            foreach (ComponentPlayer player in m_subsystemPlayers.ComponentPlayers)
            {
                m_pending[player] = count;
                player.ComponentGui.DisplayLargeMessage(
                    "La oleada empezo,",
                    "ellos vienen...", 6f, 1f);
            }
        }

        void EndWave()
        {
            m_active = false;
            m_pending.Clear();
        }

        bool TrySpawn(ComponentPlayer player)
        {
            Vector3 p = player.ComponentBody.Position;
            float angle = m_random.UniformFloat(0f, 6.2831855f);
            float dist = m_random.UniformFloat(MinDistance, MaxDistance);
            int x = (int)MathUtils.Floor(p.X + MathUtils.Cos(angle) * dist);
            int z = (int)MathUtils.Floor(p.Z + MathUtils.Sin(angle) * dist);

            TerrainChunk chunk = m_subsystemTerrain.Terrain.GetChunkAtCell(x, z);
            if (chunk == null || chunk.State < TerrainChunkState.InvalidLight) return false;
            int y = m_subsystemTerrain.Terrain.GetTopHeight(x, z) + 1;
            if (y <= 1) return false;

            string name = PickZombie();
            Entity entity = DatabaseManager.CreateEntity(Project, name, true);
            ComponentBody body = entity.FindComponent<ComponentBody>(true);
            body.Position = new Vector3(x + 0.5f, y, z + 0.5f);
            body.Rotation = Quaternion.CreateFromAxisAngle(Vector3.UnitY, m_random.UniformFloat(0f, 6.2831855f));
            Project.AddEntity(entity);

            // que vayan directo al jugador
            ComponentCreature creature = entity.FindComponent<ComponentCreature>(false);
            ComponentChaseBehavior chase = entity.FindComponent<ComponentChaseBehavior>(false);
            if (creature != null && chase != null)
                chase.Attack(player.ComponentCreature, 100f, 120f, false);
            return true;
        }

        string PickZombie()
        {
            float r = m_random.UniformFloat(0f, 1f);
            float bruto = MathUtils.Min(0.05f * (m_waveNumber - 2), 0.30f);   // desde la oleada 3
            float corredor = MathUtils.Min(0.20f + 0.03f * m_waveNumber, 0.40f);
            float gordo = m_waveNumber >= 2 ? MathUtils.Min(0.04f * (m_waveNumber - 1), 0.20f) : 0f; // desde la oleada 2
            float escupidor = m_waveNumber >= 3 ? MathUtils.Min(0.04f * (m_waveNumber - 2), 0.15f) : 0f; // desde la oleada 3
            float renacido = m_waveNumber >= 2 ? MathUtils.Min(0.05f * (m_waveNumber - 1), 0.20f) : 0f;  // desde la oleada 2
            if (r < bruto) return "ZombieKBruto";
            if (r < bruto + gordo) return GordoName;
            if (r < bruto + gordo + escupidor) return SpitterName;
            if (r < bruto + gordo + escupidor + renacido) return ReviveName;
            if (r < bruto + gordo + escupidor + renacido + corredor)
            {
                int v = m_random.Int(1, 4); // 4 variantes de camisa
                return v == 1 ? "ZombieKCorredor" : "ZombieKCorredor" + v;
            }
            return "ZombieK";
        }
    }
}
