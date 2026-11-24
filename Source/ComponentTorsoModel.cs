using Engine;

namespace Game
{
    // Modelo para criaturas que son solo un torso (zombi bruto y gordo).
    // No hereda de ComponentHumanModel, asi que NO exige los huesos
    // Leg1, Leg2, Hand1, Hand2, Head, etc. Solo necesita el modelo.
    public class ComponentTorsoModel : ComponentCreatureModel
    {
        public override void Animate()
        {
            // Posicion, rotacion y animacion de muerte del modelo completo.
            base.Animate();
        }
    }
}
