using TerrariaApi.Server;
using TShockAPI;
using Terraria.ID;
using Terraria;
using Microsoft.Xna.Framework;
namespace SpleefResurgence.Game.Editor
{
    public class ArenaEditor
    {
        enum EditMode
        {
            None,
            ArenaCorner1,
            ArenaCorner2,
            MapEdit,
            MapEditSpawnAdd,
            MapEditSpawnEdit,
            MapEditSpawnEditEdit //i swear this makes sense
        }

        private Dictionary<int, EditMode> playerEditMode = new();
        private Dictionary<int, int> playerEditMapSpawnPointID = new();
        private Dictionary<int, Action<TSPlayer, int, int>> playerTileEditCallback = new();
        private Dictionary<int, Action<TSPlayer, string>> playerChatCallback = new();

        public void Initialize()
        {
            Commands.ChatCommands.Add(new Command("spleef.edit.arena", ArenaEdit, "arena"));
            Commands.ChatCommands.Add(new Command("spleef.edit.map", MapEdit, "map"));
            GetDataHandlers.TileEdit.Register(OnTileEdit);
            ServerApi.Hooks.ServerChat.Register(Spleef.Instance, OnServerChat);
        }

        private void DeletePlayerBuff(TSPlayer player, int buffID)
        {
            for (int i = 0; i < player.TPlayer.buffType.Length; i++)
            {
                if (player.TPlayer.buffType[i] == buffID)
                {
                    player.TPlayer.DelBuff(i);
                    NetMessage.SendData(MessageID.PlayerBuffs, number: player.Index);
                    return;
                }
            }
        }

        private void RecalculateArenaCorners(int x1, int y1, int x2, int y2, out int cornerX1, out int cornerY1, out int cornerX2, out int cornerY2)
        {
            cornerX1 = Math.Min(x1, x2);
            cornerY1 = Math.Min(y1, y2);
            cornerX2 = Math.Max(x1, x2);
            cornerY2 = Math.Max(y1, y2);
        }

        private void StartArenaEditing(TSPlayer player, Arena arenaToEdit)
        {
            playerEditMode[player.Index] = EditMode.ArenaCorner1;
            player.SendInfoMessage($"Editing arena {arenaToEdit.Name}.\nSet corner 1 by breaking/placing a tile.\nType 'cancel' to cancel, 'skip' to skip to the next step, or 'lavarise <command>' to set the default lavarise command.");
            if (arenaToEdit.TilePositionX != 0 && arenaToEdit.TilePositionY != 0)
                player.SendInfoMessage($"Current corner 1 (top left) is at {arenaToEdit.TilePositionX},{arenaToEdit.TilePositionY}. You can skip to the next step if you want to keep it.");
            playerTileEditCallback[player.Index] = (plr, x, y) =>
            {
                switch (playerEditMode[plr.Index])
                {
                    case EditMode.ArenaCorner1:
                        arenaToEdit.TilePositionX = x;
                        arenaToEdit.TilePositionY = y;
                        playerEditMode[player.Index] = EditMode.ArenaCorner2;
                        plr.SendSuccessMessage($"Set corner 1 of arena {arenaToEdit.Name} to {x},{y}. Now set corner 2.");
                        if (arenaToEdit.Width != 0 && arenaToEdit.Height != 0)
                            plr.SendInfoMessage($"Current corner 2 (bottom right) is at {arenaToEdit.TilePositionX + arenaToEdit.Width},{arenaToEdit.TilePositionY + arenaToEdit.Height}. You can skip to the next step if you want to keep it.");
                        break;
                    case EditMode.ArenaCorner2:
                        int cornerX1, cornerY1, cornerX2, cornerY2;
                        RecalculateArenaCorners(arenaToEdit.TilePositionX, arenaToEdit.TilePositionY, x, y, out cornerX1, out cornerY1, out cornerX2, out cornerY2);
                        arenaToEdit.TilePositionX = cornerX1;
                        arenaToEdit.TilePositionY = cornerY1;
                        arenaToEdit.Width = cornerX2 - cornerX1;
                        arenaToEdit.Height = cornerY2 - cornerY1;
                        GameConfig.ArenaJson.SaveArena(arenaToEdit);
                        playerEditMode[player.Index] = EditMode.None;
                        plr.SendSuccessMessage($"Set corner 2 of arena {arenaToEdit.Name} to {x},{y}. Arena saved.");
                        plr.SendInfoMessage($"The arena corners are set up from left top to right bottom, if you did them the other way they'll be recalculated automatically\nYou can check with /arena info {arenaToEdit.Name}");
                        playerTileEditCallback.Remove(plr.Index);
                        break;
                }
            };

            playerChatCallback[player.Index] = (plr, text) =>
            {
                if (text == "cancel")
                {
                    playerEditMode[plr.Index] = EditMode.None;
                    plr.SendInfoMessage($"Cancelled editing arena {arenaToEdit.Name}");
                    playerChatCallback.Remove(plr.Index);
                    playerTileEditCallback.Remove(plr.Index);
                    return;
                }
                if (text == "skip")
                {
                    if (playerEditMode[plr.Index] == EditMode.ArenaCorner1)
                    {
                        playerEditMode[plr.Index] = EditMode.ArenaCorner2;
                        plr.SendInfoMessage($"Skipped setting corner 1 of arena {arenaToEdit.Name}. Now set corner 2.");
                    }
                    else if (playerEditMode[plr.Index] == EditMode.ArenaCorner2)
                    {
                        GameConfig.ArenaJson.SaveArena(arenaToEdit);
                        playerEditMode[plr.Index] = EditMode.None;
                        plr.SendSuccessMessage($"Skipped setting corner 2 of arena {arenaToEdit.Name}. Arena saved.");
                        playerChatCallback.Remove(plr.Index);
                        playerTileEditCallback.Remove(plr.Index);
                    }
                }
                if (text.StartsWith("lavarise "))
                {
                    arenaToEdit.DefaultCustomLavariseCommand = text.Substring(9);
                    GameConfig.ArenaJson.SaveArena(arenaToEdit);
                    plr.SendSuccessMessage($"Set default lavarise command of arena {arenaToEdit.Name} to {arenaToEdit.DefaultCustomLavariseCommand}");
                }

                plr.SendInfoMessage($"Editing arena {arenaToEdit.Name}.\nCurrent state: {playerEditMode[plr.Index]}.\n Type 'cancel' to cancel, 'skip' to skip, or 'lavarise <command>' to set the default lavarise command.");
            };
        }

        public void ArenaEdit(CommandArgs args)
        {
            var player = args.Player;
            if (args.Parameters.Count < 1)
            {
                args.Player.SendErrorMessage("Usage: /arena help");
                return;
            }

            if (args.Parameters[0] == "help")
            {
                player.SendInfoMessage("help mesgeae");
                return;
            }
            else if (args.Parameters[0] == "new")
            {
                if (args.Parameters.Count < 2)
                {
                    args.Player.SendErrorMessage("Usage: /arena new <arenaName>");
                    return;
                }
                string arenaName = args.Parameters[1];
                if (GameConfig.ArenaJson.ListArenaNames().Contains(arenaName))
                {
                    args.Player.SendErrorMessage($"Arena {arenaName} already exists.");
                    return;
                }

                Arena arena = new(arenaName, 0, 0, 0, 0, new());
                GameConfig.ArenaJson.SaveArena(arena);
                args.Player.SendSuccessMessage($"Created a new arena {arenaName}");
                StartArenaEditing(player, arena);
                return;
            }
            else if (args.Parameters[0] == "delete")
            {
                if (args.Parameters.Count < 2)
                {
                    args.Player.SendErrorMessage("Usage: /arena delete <arenaName>");
                    return;
                }
                string arenaToDelete = args.Parameters[1];
                if (GameConfig.ArenaJson.DeleteArena(arenaToDelete))
                    args.Player.SendSuccessMessage($"Deleted arena {arenaToDelete}");
                else
                    args.Player.SendErrorMessage($"Arena {arenaToDelete} does not exist.");
            }
            else if (args.Parameters[0] == "info")
            {
                if (args.Parameters.Count < 2)
                {
                    args.Player.SendErrorMessage("Usage: /arena info <arenaName>");
                    return;
                }
                string arenaName = args.Parameters[1];
                if (!GameConfig.ArenaJson.ListArenaNames().Contains(arenaName))
                {
                    args.Player.SendErrorMessage($"Arena {arenaName} does not exist.");
                    return;
                }
                Arena arena = GameConfig.ArenaJson.LoadArena(arenaName);
                args.Player.SendInfoMessage($"Arena {arena.Name}:");
                args.Player.SendInfoMessage($"- Pos: {arena.TilePositionX}, {arena.TilePositionY}");
                args.Player.SendInfoMessage($"- Size: {arena.Width}x{arena.Height}");
                args.Player.SendInfoMessage($"- Default lavarise command: {arena.DefaultCustomLavariseCommand}");
                args.Player.SendInfoMessage($"- Maps: {string.Join(", ", arena.MapNames)}");
            }
            else if (args.Parameters[0] == "edit")
            {
                if (args.Parameters.Count < 2)
                {
                    args.Player.SendErrorMessage("Usage: /arena edit <arenaName>");
                    return;
                }

                if (!GameConfig.ArenaJson.ListArenaNames().Contains(args.Parameters[1]))
                {
                    args.Player.SendErrorMessage($"Arena {args.Parameters[1]} does not exist.");
                    return;
                }
                Arena arenaToEdit = GameConfig.ArenaJson.LoadArena(args.Parameters[1]);
                StartArenaEditing(player, arenaToEdit);
            }

            else if (args.Parameters[0] == "addmap")
            {
                if (args.Parameters.Count < 3)
                {
                    args.Player.SendErrorMessage("Usage: /arena addmap <arenaName> <mapName>");
                    return;
                }
                if (!GameConfig.ArenaJson.ListArenaNames().Contains(args.Parameters[1]))
                {
                    args.Player.SendErrorMessage($"Arena {args.Parameters[1]} does not exist.");
                    return;
                }
                if (!GameConfig.MapJson.ListMapNames(args.Parameters[1]).Contains(args.Parameters[2]))
                {
                    args.Player.SendErrorMessage($"Map {args.Parameters[2]} does not exist for arena {args.Parameters[1]}.");
                    return;
                }
                Arena arena = GameConfig.ArenaJson.LoadArena(args.Parameters[1]);
                arena.MapNames.Add(args.Parameters[2]);
                GameConfig.ArenaJson.SaveArena(arena);
                args.Player.SendSuccessMessage($"Added map {args.Parameters[2]} to arena {args.Parameters[1]}");
            }
            else
            {
                args.Player.SendErrorMessage("idfk what you said");
            }
        }

        private void StartMapEditing(TSPlayer player, Map mapToEdit, string arenaName)
        {
            playerEditMode[player.Index] = EditMode.MapEdit;
            player.SendInfoMessage($"Editing map {mapToEdit.Name}.\nType 'cancel' to cancel, 'spawnadd' to set a spawn at where you're standing or 'spawnedit' to edit an existing one");
            playerChatCallback[player.Index] = (plr, text) =>
            {
                if (text == "cancel")
                {
                    playerEditMode[plr.Index] = EditMode.None;
                    plr.SendInfoMessage($"Cancelled editing map {mapToEdit.Name}");
                    DeletePlayerBuff(plr, BuffID.Webbed);
                    playerChatCallback.Remove(plr.Index);
                    return;
                }
                if (playerEditMode[plr.Index] == EditMode.MapEdit)
                {
                    if (text == "spawnadd")
                    {
                        int x = plr.TileX;
                        int y = plr.TileY;
                        plr.Teleport(x * 16, y * 16);
                        plr.SetBuff(BuffID.Webbed, 100000);
                        player.SendInfoMessage($"Teleported to the tile spawn point {x}, {y}, type 'confirm' or 'undo'");
                        playerEditMode[plr.Index] = EditMode.MapEditSpawnAdd;
                        return;
                    }
                    if (text == "spawnedit")
                    {
                        if (mapToEdit.Spawns.Count == 0)
                        {
                            plr.SendErrorMessage($"Map {mapToEdit.Name} has no spawn points to edit.");
                            return;
                        }
                        plr.SendInfoMessage($"Map {mapToEdit.Name} has the following spawn points:");
                        for (int i = 0; i < mapToEdit.Spawns.Count; i++)
                        {
                            plr.SendMessage($"{i}: {mapToEdit.Spawns[i].X}, {mapToEdit.Spawns[i].Y}", Color.Aqua);
                        }
                        plr.SendInfoMessage("Type 'spawnedit <index>' to edit a spawn point.");
                        return;
                    }
                    if (text.StartsWith("spawnedit "))
                    {
                        if (mapToEdit.Spawns.Count == 0)
                        {
                            plr.SendErrorMessage($"Map {mapToEdit.Name} has no spawn points to edit.");
                            return;
                        }
                        if (!int.TryParse(text.Substring(10), out int index))
                        {
                            plr.SendErrorMessage($"Invalid index {text.Substring(10)}.");
                            return;
                        }
                        if (index < 0 || index >= mapToEdit.Spawns.Count)
                        {
                            plr.SendErrorMessage($"Index {index} is out of range.");
                            return;
                        }
                        int x = mapToEdit.Spawns[index].X;
                        int y = mapToEdit.Spawns[index].Y;
                        plr.Teleport(x * 16, y * 16);
                        plr.SetBuff(BuffID.Webbed, 100000);
                        player.SendInfoMessage($"Teleported to the current tile spawn point {x},{y}, type 'edit' or 'undo'");
                        playerEditMapSpawnPointID[plr.Index] = index;
                        playerEditMode[plr.Index] = EditMode.MapEditSpawnEdit;
                        return;
                    }
                }
                else if (playerEditMode[plr.Index] == EditMode.MapEditSpawnAdd)
                {
                    if (text == "confirm")
                    {
                        if (playerEditMapSpawnPointID.ContainsKey(plr.Index))
                        {
                            int index = playerEditMapSpawnPointID[plr.Index];
                            mapToEdit.Spawns[index] = new(plr.TileX, plr.TileY);
                            plr.SendSuccessMessage($"Edited spawn point {index} to {plr.TileX},{plr.TileY} in map {mapToEdit.Name}");
                            playerEditMapSpawnPointID.Remove(plr.Index);
                        }
                        else
                        {
                            int x = plr.TileX;
                            int y = plr.TileY;
                            mapToEdit.Spawns.Add(new(x, y));
                            plr.SendSuccessMessage($"Added spawn point at {x},{y} to map {mapToEdit.Name}");
                        }
                        DeletePlayerBuff(plr, BuffID.Webbed);
                        GameConfig.MapJson.SaveMap(mapToEdit, arenaName);
                        playerEditMode[plr.Index] = EditMode.MapEdit;
                    }
                    if (text == "undo")
                    {                 
                        DeletePlayerBuff(plr, BuffID.Webbed);
                        plr.SendInfoMessage($"Cancelled adding spawn point");
                        playerEditMode[plr.Index] = EditMode.MapEdit;
                        playerEditMapSpawnPointID.Remove(plr.Index);
                        return;
                    }
                }
                else if (playerEditMode[plr.Index] == EditMode.MapEditSpawnEdit)
                {
                    if (text == "edit")
                    {
                        DeletePlayerBuff(plr, BuffID.Webbed);
                        playerEditMode[plr.Index] = EditMode.MapEditSpawnEditEdit;
                        plr.SendInfoMessage($"Editing spawn point, type 'set' to set a new spawn point or 'undo' to cancel");
                        return;
                    }
                    if (text == "undo")
                    {
                        DeletePlayerBuff(plr, BuffID.Webbed);
                        plr.SendInfoMessage($"Cancelled editing spawn point");
                        playerEditMode[plr.Index] = EditMode.MapEdit;
                        playerEditMapSpawnPointID.Remove(plr.Index);
                        return;
                    }
                }
                else if (playerEditMode[plr.Index] == EditMode.MapEditSpawnEditEdit)
                {
                    if (text == "set")
                    {
                        int x = plr.TileX;
                        int y = plr.TileY;
                        plr.Teleport(x * 16, y * 16);
                        plr.SetBuff(BuffID.Webbed, 100000);
                        player.SendInfoMessage($"Teleported to the tile spawn point {x},{y}, type 'confirm' or 'undo'");
                        playerEditMode[plr.Index] = EditMode.MapEditSpawnAdd;
                        return;
                    }
                    if (text == "undo")
                    {
                        plr.SendInfoMessage($"Cancelled editing spawn point");
                        playerEditMode[plr.Index] = EditMode.MapEdit;
                        playerEditMapSpawnPointID.Remove(plr.Index);
                        return;
                    }

                }
            };
        }

        public void MapEdit(CommandArgs args) //haha mapedit from penguin games
        {
            var player = args.Player;
            if (args.Parameters.Count < 1)
            {
                args.Player.SendErrorMessage("Usage: /map help");
                return;
            }
            if (args.Parameters[0] == "help")
            {
                player.SendInfoMessage("help msegae");
                return;
            }
            else if (args.Parameters[0] == "new")
            {
                if (args.Parameters.Count < 3)
                {
                    args.Player.SendErrorMessage("Usage: /map new <arenaName> <mapName>");
                    return;
                }
                string mapName = args.Parameters[2];
                string arenaName = args.Parameters[1];
                if (!GameConfig.ArenaJson.ListArenaNames().Contains(arenaName))
                {
                    args.Player.SendErrorMessage($"Arena {arenaName} does not exist.");
                    return;
                }

                if (GameConfig.MapJson.ListMapNames(arenaName).Contains(mapName))
                {
                    args.Player.SendErrorMessage($"Map {mapName} already exists for the arena {arenaName}.");
                    return;
                }

                Map map = new(mapName, "", player.Account.Name, RiseType.Lava, new(), new());
                
                GameConfig.MapJson.SaveMap(map, arenaName);
                args.Player.SendSuccessMessage($"Created a new map {mapName}");
                StartMapEditing(player, map, arenaName);
                return;
            }
            else if (args.Parameters[0] == "delete")
            {
                if (args.Parameters.Count < 3)
                {
                    args.Player.SendErrorMessage("Usage: /map delete <arenaName> <mapName>");
                    return;
                }
                string mapName = args.Parameters[2];
                string arenaName = args.Parameters[1];
                if (!GameConfig.ArenaJson.ListArenaNames().Contains(arenaName))
                {
                    args.Player.SendErrorMessage($"Arena {arenaName} does not exist.");
                    return;
                }
                if (!GameConfig.MapJson.ListMapNames(arenaName).Contains(mapName))
                {
                    args.Player.SendErrorMessage($"Map {mapName} does not exist for the arena {arenaName}.");
                    return;
                }

                if (player.Account.Name != GameConfig.MapJson.LoadMap(arenaName, mapName).OwnerName && !player.HasPermission("spleef.edit.admin"))
                {
                    args.Player.SendErrorMessage($"You do not have permission to delete maps that are not made by you");
                    return;
                }

                if (!GameConfig.MapJson.DeleteMap(arenaName, mapName))
                {
                    args.Player.SendErrorMessage($"Failed to delete map {mapName} from arena {arenaName}");
                    return;
                }
                args.Player.SendSuccessMessage($"Deleted map {mapName} from arena {arenaName}");
            }
            else if (args.Parameters[0] == "edit")
            {
                if (args.Parameters.Count < 3)
                {
                    args.Player.SendErrorMessage("Usage: /map edit <arenaName> <mapName>");
                    return;
                }
                string arenaName = args.Parameters[1];
                string mapName = args.Parameters[2];
                if (!GameConfig.ArenaJson.ListArenaNames().Contains(arenaName))
                {
                    args.Player.SendErrorMessage($"Arena {arenaName} does not exist.");
                    return;
                }
                if (!GameConfig.MapJson.ListMapNames(arenaName).Contains(mapName))
                {
                    args.Player.SendErrorMessage($"Map {mapName} does not exist for the arena {arenaName}.");
                    return;
                }
                Map mapToEdit = GameConfig.MapJson.LoadMap(arenaName, mapName);
                if (player.Account.Name != mapToEdit.OwnerName && !player.HasPermission("spleef.edit.admin"))
                {
                    args.Player.SendErrorMessage($"You do not have permission to edit maps that are not made by you");
                    return;
                }
                StartMapEditing(player, mapToEdit, arenaName);
            }
            else
            {
                player.SendErrorMessage("idfk what you said");
            }   
        }

        private void OnTileEdit(object sender, GetDataHandlers.TileEditEventArgs args)
        {
            if (!playerEditMode.ContainsKey(args.Player.Index))
                return;

            if (playerEditMode[args.Player.Index] == EditMode.None)
                return;

            if (!playerTileEditCallback.ContainsKey(args.Player.Index))
                return;

            playerTileEditCallback[args.Player.Index](args.Player, args.X, args.Y);
            args.Handled = true;
            NetMessage.SendTileSquare(args.Player.Index, args.X, args.Y, 1);
        }

        private void OnServerChat(ServerChatEventArgs args)
        {
            if (!playerEditMode.ContainsKey(args.Who))
                return;

            if (playerEditMode[args.Who] == EditMode.None)
                return;

            if (!playerChatCallback.ContainsKey(args.Who))
                return;

            if (args.Text.StartsWith("/"))
                return;

            playerChatCallback[args.Who](TShock.Players[args.Who], args.Text);
            args.Handled = true;
        }
    }
}
