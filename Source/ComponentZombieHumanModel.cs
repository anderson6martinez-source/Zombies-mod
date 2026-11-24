using Engine;
using Engine.Graphics;

namespace Game
{
    // Modelo humano de los zombis: siempre con los brazos levantados al frente.
    // Se usa desde Zombies.xdb (parametro Class del componente HumanModel).
    public class ComponentZombieHumanModel : ComponentHumanModel
    {
        // Angulo de los brazos en radianes: 1.5 = casi horizontales al frente.
        // Si en el juego quedan hacia atras, cambia el signo (-1.5f).
        const float ArmAngle = 1.5f;
        // Balanceo suave de los brazos (0 para dejarlos totalmente quietos).
        const float ArmSway = 0.08f;

        ModelBone m_arm1;
        ModelBone m_arm2;
        ComponentHealth m_health;
        bool m_searched;

        public override void Animate()
        {
            base.Animate();

            if (!m_searched)
            {
                m_searched = true;
                m_arm1 = Model.FindBone("Hand1", false);
                m_arm2 = Model.FindBone("Hand2", false);
                m_health = Entity.FindComponent<ComponentHealth>(false);
            }

            // muerto: se deja la animacion normal
            if (m_health != null && m_health.Health <= 0f) return;

            float sway = ArmSway * MathUtils.Sin((float)Time.RealTime * 3f);
            if (m_arm1 != null)
                SetBoneTransform(m_arm1.Index, Matrix.CreateRotationX(ArmAngle + sway) * m_arm1.Transform);
            if (m_arm2 != null)
                SetBoneTransform(m_arm2.Index, Matrix.CreateRotationX(ArmAngle - sway) * m_arm2.Transform);
        }
    }
}
