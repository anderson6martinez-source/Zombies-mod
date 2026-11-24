using Engine;
using Engine.Graphics;

namespace Game
{
    // Modelo para criaturas que son solo un torso (zombi bruto y gordo).
    // No hereda de ComponentHumanModel, asi que NO exige los huesos
    // Leg1, Leg2, Hand1, Hand2, Head, etc. Solo necesita el modelo.
    public class ComponentTorsoModel : ComponentCreatureModel
    {
        ComponentBody m_body;

        public override void Animate()
        {
            base.Animate();

            if (m_body == null)
                m_body = Entity.FindComponent<ComponentBody>(true);

            // Sin esto el modelo se dibuja en el origen del mundo (0,0,0) y se ve "invisible":
            // el humano normal coloca el modelo con su hueso Body, que aqui no existe.
            // Se coloca el hueso raiz en la posicion y rotacion del cuerpo de la criatura.
            Matrix transform = Matrix.CreateFromQuaternion(m_body.Rotation) * Matrix.CreateTranslation(m_body.Position);
            SetBoneTransform(Model.RootBone.Index, transform);
        }
    }
}
