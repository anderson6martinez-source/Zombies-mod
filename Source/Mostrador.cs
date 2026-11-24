// Mostrador de intercambios para el mod Zombies.
// - MostradorBlock: bloque (ID 762) con textura de tablones. Se craftea con 8 tablones en cuadro (hueco al centro).
// - SubsystemMostradorBehavior: al tocarlo abre una lista de intercambios; al elegir uno, se cambian los items.
// Para agregar o cambiar intercambios edita el metodo BuildTrades().
using System;
using System.Collections.Generic;
using Engine;
using GameEntitySystem;
using TemplatesDatabase;

namespace Game
{
    public class MostradorBlock : CubeBlock
    {
        public const int Index = 762;

        private static int m_slot = -1;

        public override int GetFaceTextureSlot(int face, int value)
        {
            if (m_slot < 0) m_slot = BuscarTexturaTablones();
            return m_slot;
        }

        // Usa la textura de los tablones del juego (si no los encuentra, usa el slot 4)
        private static int BuscarTexturaTablones()
        {
            foreach (Block block in BlocksManager.Blocks)
            {
                if (block == null) continue;
                if (block.GetType().Name == "PlanksBlock")
                    return block.GetFaceTextureSlot(0, Terrain.MakeBlockValue(block.BlockIndex, 0, 0));
            }
            return 4;
        }

        public override string GetDisplayName(SubsystemTerrain subsystemTerrain, int value)
        {
            return "Mostrador de intercambios";
        }

        public override string GetDescription(int value)
        {
            return "Toca el mostrador para cambiar items por otros (carne podrida, balas, pistolas...).";
        }

        public override string GetCategory(int value)
        {
            return "Furniture";
        }

        // Receta: 8 tablones en cuadro (PPP / P.P / PPP)
        public override IEnumerable<CraftingRecipe> GetProceduralCraftingRecipes()
        {
            CraftingRecipe recipe = new CraftingRecipe();
            recipe.ResultValue = Terrain.MakeBlockValue(Index, 0, 0);
            recipe.ResultCount = 1;
            recipe.RequiredHeatLevel = 0f;
            recipe.Description = "Mostrador de intercambios";
            int[] casillas = { 0, 1, 2, 3, 5, 6, 7, 8 };
            foreach (int i in casillas) recipe.Ingredients[i] = Ingredientes.Tablon;
            yield return recipe;
        }
    }

    public class SubsystemMostradorBehavior : SubsystemBlockBehavior
    {
        private class Trade
        {
            public int GiveValue, GiveCount;   // lo que pagas
            public int GetValue, GetCount;     // lo que recibes
        }

        private SubsystemPickables m_subsystemPickables;

        public override int[] HandledBlocks
        {
            get { return new int[] { MostradorBlock.Index }; }
        }

        public override void Load(ValuesDictionary valuesDictionary)
        {
            base.Load(valuesDictionary);
            m_subsystemPickables = Project.FindSubsystem<SubsystemPickables>(true);
        }

        // ---- LISTA DE INTERCAMBIOS: edita aqui ----
        // Item(nombreDeClaseDelBloque) busca cualquier bloque del juego por su nombre de clase.
        // Si un item no existe en tu version, ese intercambio simplemente no aparece.
        private List<Trade> BuildTrades()
        {
            List<Trade> list = new List<Trade>();
            int carne = Item("RottenMeatBlock");
            int bala = Terrain.MakeBlockValue(BalaPistolaBlock.Index, 0, 0);
            int pistola = Terrain.MakeBlockValue(PistolaBlock.Index, 0, 0);
            int pan = Item("BreadBlock");

            Add(list, carne, 5, bala, 4);      // 5 carne podrida -> 4 balas
            Add(list, carne, 16, pistola, 1);  // 16 carne podrida -> 1 pistola
            Add(list, bala, 8, pan, 3);        // 8 balas -> 3 pan
            return list;
        }

        private static void Add(List<Trade> list, int giveValue, int giveCount, int getValue, int getCount)
        {
            if (giveValue == 0 || getValue == 0) return; // item no encontrado
            list.Add(new Trade { GiveValue = giveValue, GiveCount = giveCount, GetValue = getValue, GetCount = getCount });
        }

        private static int Item(string className)
        {
            foreach (Block block in BlocksManager.Blocks)
            {
                if (block != null && block.GetType().Name == className)
                    return Terrain.MakeBlockValue(block.BlockIndex, 0, 0);
            }
            return 0;
        }

        private string NameOf(int value)
        {
            return BlocksManager.Blocks[Terrain.ExtractContents(value)].GetDisplayName(SubsystemTerrain, value);
        }

        public override bool OnInteract(TerrainRaycastResult raycastResult, ComponentMiner componentMiner)
        {
            ComponentPlayer player = componentMiner.ComponentPlayer;
            if (player == null) return false;
            List<Trade> trades = BuildTrades();
            DialogsManager.ShowDialog(player.View.GameWidget, new ListSelectionDialog(
                "Intercambios", trades, 56f,
                delegate (object o)
                {
                    Trade t = (Trade)o;
                    return new LabelWidget
                    {
                        Text = t.GiveCount + " x " + NameOf(t.GiveValue) + "   ->   " + t.GetCount + " x " + NameOf(t.GetValue),
                        HorizontalAlignment = WidgetAlignment.Center,
                        VerticalAlignment = WidgetAlignment.Center
                    };
                },
                delegate (object o) { DoTrade((Trade)o, componentMiner, player); }));
            return true;
        }

        private void DoTrade(Trade t, ComponentMiner miner, ComponentPlayer player)
        {
            IInventory inventory = miner.Inventory;
            if (inventory == null) return;
            int contents = Terrain.ExtractContents(t.GiveValue);

            int have = 0;
            for (int i = 0; i < inventory.SlotsCount; i++)
                if (Terrain.ExtractContents(inventory.GetSlotValue(i)) == contents) have += inventory.GetSlotCount(i);

            if (have < t.GiveCount)
            {
                player.ComponentGui.DisplaySmallMessage("Te faltan " + (t.GiveCount - have) + " x " + NameOf(t.GiveValue), Color.White, false, false);
                return;
            }

            // cobrar
            int left = t.GiveCount;
            for (int i = 0; i < inventory.SlotsCount && left > 0; i++)
            {
                if (Terrain.ExtractContents(inventory.GetSlotValue(i)) != contents) continue;
                int n = MathUtils.Min(inventory.GetSlotCount(i), left);
                inventory.RemoveSlotItems(i, n);
                left -= n;
            }

            // entregar; lo que no quepa en el inventario cae al suelo
            int notAdded = ComponentInventoryBase.AcquireItems(inventory, t.GetValue, t.GetCount);
            if (notAdded > 0)
            {
                Vector3 pos = miner.ComponentCreature.ComponentBody.Position + new Vector3(0f, 1f, 0f);
                m_subsystemPickables.AddPickable(t.GetValue, notAdded, pos, null, null);
            }
            player.ComponentGui.DisplaySmallMessage("Intercambio hecho: " + t.GetCount + " x " + NameOf(t.GetValue), Color.White, false, false);
        }
    }
}
