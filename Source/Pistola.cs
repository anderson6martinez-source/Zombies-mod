// Pistola + bala para el mod Zombies (Survivalcraft 2.3 / API 1.5)
// - PistolaBlock: arma, 30 de durabilidad, sin estado de carga (no hay bugs de recarga)
// - BalaPistolaBlock: bala, 7 de dano
// - SubsystemPistolaBlockBehavior: al apuntar y soltar, gasta 1 bala del inventario y dispara
using System;
using System.Collections.Generic;
using Engine;
using Engine.Graphics;
using GameEntitySystem;
using TemplatesDatabase;

namespace Game
{
    public class PistolaBlock : Block
    {
        public const int Index = 760;

        // Si el arma se ve girada en la mano, cambia estos grados (0, 90, 180, 270).
        private const float RotX = 0f;
        private const float RotY = 180f;
        private const float RotZ = 0f;

        private BlockMesh m_mesh = new BlockMesh();
        private Texture2D m_texture;

        public PistolaBlock()
        {
            Durability = 65;
            MaxStacking = 1;
            IsAimable = true;
        }

        public override void Initialize()
        {
            Model model = ContentManager.Get<Model>("Models/Pistola");
            ModelMesh mesh = model.Meshes[0];
            Matrix bone = BlockMesh.GetBoneAbsoluteTransform(mesh.ParentBone)
                * Matrix.CreateRotationX(MathUtils.DegToRad(RotX))
                * Matrix.CreateRotationY(MathUtils.DegToRad(RotY))
                * Matrix.CreateRotationZ(MathUtils.DegToRad(RotZ));
            foreach (ModelMeshPart part in mesh.MeshParts)
            {
                m_mesh.AppendModelMeshPart(part, bone, false, false, false, false, Color.White);
            }
            m_texture = ContentManager.Get<Texture2D>("Textures/Pistola");
            base.Initialize();
        }

        public override void DrawBlock(PrimitivesRenderer3D primitivesRenderer, int value, Color color, float size, ref Matrix matrix, DrawBlockEnvironmentData environmentData)
        {
            BlocksManager.DrawMeshBlock(primitivesRenderer, m_mesh, m_texture, color, 2f * size, ref matrix, environmentData);
        }

        // El bit 0 de los datos indica si esta cargada (los bits altos guardan el desgaste)
        public static bool IsLoaded(int value)
        {
            return (Terrain.ExtractData(value) & 1) != 0;
        }

        public static int SetLoaded(int value, bool loaded)
        {
            int data = Terrain.ExtractData(value);
            data = loaded ? (data | 1) : (data & ~1);
            return Terrain.MakeBlockValue(Terrain.ExtractContents(value), 0, data);
        }

        public override string GetDisplayName(SubsystemTerrain subsystemTerrain, int value)
        {
            return IsLoaded(value) ? "Pistola (cargada)" : "Pistola";
        }

        public override string GetDescription(int value)
        {
            return "Pistola. Mantén apuntar para cargar una bala del inventario, como una ballesta; suelta para disparar. 65 usos.";
        }

        public override string GetCategory(int value)
        {
            return "Tools";
        }

        // Receta: AAA / .BA / ...   (A = lingote de hierro, B = ironrod)
        public override IEnumerable<CraftingRecipe> GetProceduralCraftingRecipes()
        {
            CraftingRecipe recipe = new CraftingRecipe();
            recipe.ResultValue = Terrain.MakeBlockValue(Index, 0, 0);
            recipe.ResultCount = 1;
            recipe.RequiredHeatLevel = 0f;
            recipe.Description = "Pistola";
            recipe.Ingredients[0] = "ironingot";
            recipe.Ingredients[1] = "ironingot";
            recipe.Ingredients[2] = "ironingot";
            recipe.Ingredients[7] = "ironrod";   // fila 2, columna 2
            recipe.Ingredients[8] = "ironingot"; // fila 2, columna 3
            yield return recipe;
        }
    }

    public class BalaPistolaBlock : Block
    {
        public const int Index = 761;

        private BlockMesh m_mesh = new BlockMesh();
        private Texture2D m_texture;

        public override void Initialize()
        {
            Model model = ContentManager.Get<Model>("Models/Bala");
            ModelMesh mesh = model.Meshes[0];
            Matrix bone = BlockMesh.GetBoneAbsoluteTransform(mesh.ParentBone);
            foreach (ModelMeshPart part in mesh.MeshParts)
            {
                m_mesh.AppendModelMeshPart(part, bone, false, false, false, false, Color.White);
            }
            m_texture = ContentManager.Get<Texture2D>("Textures/Pistola");
            base.Initialize();
        }

        public override void DrawBlock(PrimitivesRenderer3D primitivesRenderer, int value, Color color, float size, ref Matrix matrix, DrawBlockEnvironmentData environmentData)
        {
            BlocksManager.DrawMeshBlock(primitivesRenderer, m_mesh, m_texture, color, 4f * size, ref matrix, environmentData);
        }

        public override float GetProjectilePower(int value)
        {
            return 7f;
        }

        public override string GetDisplayName(SubsystemTerrain subsystemTerrain, int value)
        {
            return "Bala de pistola";
        }

        public override string GetDescription(int value)
        {
            return "Munición para la pistola. 7 de daño.";
        }

        public override string GetCategory(int value)
        {
            return "Tools";
        }

        // Receta: lingote de hierro + ironrod (en vertical) = 4 balas
        public override IEnumerable<CraftingRecipe> GetProceduralCraftingRecipes()
        {
            CraftingRecipe recipe = new CraftingRecipe();
            recipe.ResultValue = Terrain.MakeBlockValue(Index, 0, 0);
            recipe.ResultCount = 4;
            recipe.RequiredHeatLevel = 0f;
            recipe.Description = "Balas de pistola";
            recipe.Ingredients[0] = "ironingot"; // fila 1, columna 1
            recipe.Ingredients[6] = "ironrod";   // fila 2, columna 1
            yield return recipe;
        }
    }

    public class SubsystemPistolaBlockBehavior : SubsystemBlockBehavior
    {
        // Segundos que hay que mantener apuntando para cargar una bala
        private const double ReloadTime = 0.8;

        private SubsystemProjectiles m_subsystemProjectiles;
        private SubsystemAudio m_subsystemAudio;
        private Random m_random = new Random();
        private Dictionary<ComponentMiner, double> m_aimStart = new Dictionary<ComponentMiner, double>();

        public override int[] HandledBlocks
        {
            get { return new int[] { PistolaBlock.Index }; }
        }

        public override void Load(ValuesDictionary valuesDictionary)
        {
            base.Load(valuesDictionary);
            m_subsystemProjectiles = Project.FindSubsystem<SubsystemProjectiles>(true);
            m_subsystemAudio = Project.FindSubsystem<SubsystemAudio>(true);
        }

        // Como un arco/ballesta: al apuntar se carga una bala del inventario; al soltar se dispara.
        public override bool OnAim(Ray3 aim, ComponentMiner componentMiner, AimState state)
        {
            Vector3 start = aim.Position;
            Vector3 direction = aim.Direction;
            IInventory inventory = componentMiner.Inventory;
            if (inventory == null) return false;
            int slot = inventory.ActiveSlotIndex;
            if (slot < 0 || inventory.GetSlotCount(slot) <= 0) return false;
            int value = inventory.GetSlotValue(slot);
            if (Terrain.ExtractContents(value) != PistolaBlock.Index) return false;

            switch (state)
            {
                case AimState.InProgress:
                    if (!PistolaBlock.IsLoaded(value))
                    {
                        double t;
                        if (!m_aimStart.TryGetValue(componentMiner, out t))
                        {
                            t = Time.FrameStartTime;
                            m_aimStart[componentMiner] = t;
                        }
                        if (Time.FrameStartTime - t >= ReloadTime && TakeBullet(inventory))
                        {
                            ReplaceSlot(inventory, slot, PistolaBlock.SetLoaded(value, true));
                        }
                    }
                    return true;

                case AimState.Cancelled:
                    m_aimStart.Remove(componentMiner);
                    return true;

                case AimState.Completed:
                    m_aimStart.Remove(componentMiner);
                    if (PistolaBlock.IsLoaded(value))
                    {
                        ReplaceSlot(inventory, slot, PistolaBlock.SetLoaded(value, false));
                        Fire(start, direction, componentMiner);
                        componentMiner.DamageActiveTool(1);
                    }
                    return true;
            }
            return false;
        }

        // Tambien lo puede llamar la IA del superviviente (dispara sin gastar bala del inventario).
        public void Fire(Vector3 start, Vector3 direction, ComponentMiner componentMiner)
        {
            direction = Vector3.Normalize(direction);
            Vector3 spread = new Vector3(m_random.Float(-0.01f, 0.01f), m_random.Float(-0.01f, 0.01f), m_random.Float(-0.01f, 0.01f));
            Vector3 position = start + direction * 0.4f;
            Vector3 velocity = Vector3.Normalize(direction + spread) * 90f;
            int bulletValue = Terrain.MakeBlockValue(BalaPistolaBlock.Index, 0, 0);
            m_subsystemProjectiles.FireProjectile(bulletValue, position, velocity, Vector3.Zero, componentMiner.ComponentCreature);
            try { m_subsystemAudio.PlaySound("Audio/MusketFire", 1f, m_random.Float(-0.1f, 0.1f), position, 15f, true); } catch (Exception) { }
        }

        private static bool TakeBullet(IInventory inventory)
        {
            for (int i = 0; i < inventory.SlotsCount; i++)
            {
                if (inventory.GetSlotCount(i) > 0 && Terrain.ExtractContents(inventory.GetSlotValue(i)) == BalaPistolaBlock.Index)
                {
                    inventory.RemoveSlotItems(i, 1);
                    return true;
                }
            }
            return false;
        }

        private static void ReplaceSlot(IInventory inventory, int slot, int newValue)
        {
            inventory.RemoveSlotItems(slot, 1);
            inventory.AddSlotItems(slot, newValue, 1);
        }
    }
}
