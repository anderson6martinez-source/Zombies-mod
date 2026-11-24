// Mostrador de intercambios para el mod Zombies.
// - MostradorBlock: bloque (ID 762) con textura de tablones.
// - SubsystemMostradorBehavior: al tocarlo abre una lista de intercambios; al elegir uno, se cambian los items.
// Para agregar o cambiar intercambios edita el metodo BuildTrades().
using System;
using System.Collections.Generic;
using System.IO;
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
        // Item("NombreDeClase") busca cualquier bloque del juego por su nombre de clase (puedes dar varios nombres alternativos).
        // Si un item no existe en tu version, ese intercambio simplemente no aparece.
        private List<Trade> BuildTrades()
        {
            List<Trade> list = new List<Trade>();
            int cuero = Item("LeatherBlock");
            int carbon = Item("CoalChunkBlock", "CoalBlock");
            int cobre = Item("CopperIngotBlock", "CopperBlock");
            int pan = Item("BreadBlock");

            Add(list, cuero, 20, pan, 1);   // 20 cuero -> 1 pan
            Add(list, carbon, 20, cobre, 4); // 20 carbon -> 4 cobre
            Add(list, cobre, 1, pan, 4);     // 1 cobre -> 4 panes
            return list;
        }

        private static void Add(List<Trade> list, int giveValue, int giveCount, int getValue, int getCount)
        {
            if (giveValue == 0 || getValue == 0) return; // item no encontrado
            list.Add(new Trade { GiveValue = giveValue, GiveCount = giveCount, GetValue = getValue, GetCount = getCount });
        }

        private static int Item(params string[] classNames)
        {
            foreach (string className in classNames)
            {
                foreach (Block block in BlocksManager.Blocks)
                {
                    if (block != null && block.GetType().Name == className)
                        return Terrain.MakeBlockValue(block.BlockIndex, 0, 0);
                }
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
            ListSelectionDialog dialogo = new ListSelectionDialog(
                "Intercambios", trades, 56f,
                delegate (object o)
                {
                    Trade t = (Trade)o;
                    StackPanelWidget fila = new StackPanelWidget
                    {
                        Direction = LayoutDirection.Horizontal,
                        HorizontalAlignment = WidgetAlignment.Center,
                        VerticalAlignment = WidgetAlignment.Center
                    };
                    fila.Children.Add(new BlockIconWidget
                    {
                        Value = t.GiveValue,
                        Size = new Vector2(44f),
                        VerticalAlignment = WidgetAlignment.Center
                    });
                    fila.Children.Add(new LabelWidget
                    {
                        Text = " " + t.GiveCount + " x " + NameOf(t.GiveValue) + "   ->   ",
                        VerticalAlignment = WidgetAlignment.Center
                    });
                    fila.Children.Add(new BlockIconWidget
                    {
                        Value = t.GetValue,
                        Size = new Vector2(44f),
                        VerticalAlignment = WidgetAlignment.Center
                    });
                    fila.Children.Add(new LabelWidget
                    {
                        Text = " " + t.GetCount + " x " + NameOf(t.GetValue),
                        VerticalAlignment = WidgetAlignment.Center
                    });
                    return fila;
                },
                delegate (object o) { DoTrade((Trade)o, componentMiner, player); });
            // Logo de la tienda (carrito sin fondo) en la esquina superior izquierda
            dialogo.Children.Add(new RectangleWidget
            {
                Subtexture = new Subtexture(GetLogo(), Vector2.Zero, Vector2.One),
                FillColor = Color.White,
                OutlineColor = Color.Transparent,
                Size = new Vector2(64f),
                HorizontalAlignment = WidgetAlignment.Near,
                VerticalAlignment = WidgetAlignment.Near,
                Margin = new Vector2(12f, 12f)
            });
            DialogsManager.ShowDialog(player.GameWidget, dialogo);
            return true;
        }

        // ---- LOGO: carrito PNG con fondo transparente, incrustado en base64 ----
        private static Texture2D m_logo;

        private static Texture2D GetLogo()
        {
            if (m_logo == null)
            {
                using (MemoryStream ms = new MemoryStream(Convert.FromBase64String(LogoBase64)))
                {
                    m_logo = Texture2D.Load(Engine.Media.Image.Load(ms, ImageFileFormat.Png));
                }
            }
            return m_logo;
        }

        private const string LogoBase64 = "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";

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
