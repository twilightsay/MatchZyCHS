using System.Text.Json;
using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Commands;
using CounterStrikeSharp.API.Modules.Memory;
using CounterStrikeSharp.API.Modules.Utils;
using CounterStrikeSharp.API.Modules.Timers;
using CounterStrikeSharp.API.Modules.Cvars;
using CounterStrikeSharp.API.Modules.Admin;
using System.Text.RegularExpressions;
using System.Text;
using Newtonsoft.Json.Linq;


namespace MatchZy
{
    public partial class MatchZy
    {
        public const string warmupCfgPath = "MatchZy/warmup.cfg";
        public const string knifeCfgPath = "MatchZy/knife.cfg";
        public const string liveCfgPath = "MatchZy/live.cfg";

        private void LoadAdmins() {
            string fileName = "MatchZy/admins.json";
            string filePath = Path.Join(Server.GameDirectory + "/csgo/cfg", fileName);

            if (File.Exists(filePath)) {
                try {
                    using (StreamReader fileReader = File.OpenText(filePath)) {
                        string jsonContent = fileReader.ReadToEnd();
                        if (!string.IsNullOrEmpty(jsonContent)) {
                            JsonSerializerOptions options = new()
                            {
                                AllowTrailingCommas = true,
                            };
                            loadedAdmins = JsonSerializer.Deserialize<Dictionary<string, string>>(jsonContent, options) ?? new Dictionary<string, string>();
                        }
                        else {
                            // Handle the case where the JSON content is empty or null
                            loadedAdmins = new Dictionary<string, string>();
                        }
                    }
                    foreach (var kvp in loadedAdmins) {
                        Log($"[ADMIN] Username: {kvp.Key}, Role: {kvp.Value}");
                    }
                }
                catch (Exception e) {
                    Log($"[LoadAdmins FATAL] An error occurred: {e.Message}");
                }
            }
            else {
                Log("[LoadAdmins] The JSON file does not exist. Creating one with default content");
                Dictionary<string, string> defaultAdmins = new()
                {
                    { "steamid", "" }
                };

                try {
                    JsonSerializerOptions options = new()
                    {
                        WriteIndented = true,
                    };
                    string defaultJson = JsonSerializer.Serialize(defaultAdmins, options);
                    string? directoryPath = Path.GetDirectoryName(filePath);
                    if (directoryPath != null)
                    {
                        if (!Directory.Exists(directoryPath))
                        {
                            Directory.CreateDirectory(directoryPath);
                        }
                    }
                    File.WriteAllText(filePath, defaultJson);

                    Log("[LoadAdmins] Created a new JSON file with default content.");
                }
                catch (Exception e) {
                    Log($"[LoadAdmins FATAL] Error creating the JSON file: {e.Message}");
                }
            }
        }

        private bool IsPlayerAdmin(CCSPlayerController? player, string command = "", params string[] permissions) {
            string[] updatedPermissions = permissions.Concat(new[] { "@css/root" }).ToArray();
            RequiresPermissionsOr attr = new(updatedPermissions)
            {
                Command = command
            };
            if (attr.CanExecuteCommand(player)) return true; // Admin exists in admins.json of CSSharp
            if (player == null) return true; // Sent via server, hence should be treated as an admin.
            if (loadedAdmins.ContainsKey(player.SteamID.ToString())) return true; // Admin exists in admins.json of MatchZy
            return false;
        }
        
        private int GetRealPlayersCount() {
            return playerData.Count;
        }

        private void SendUnreadyPlayersMessage() {
            if (isWarmup && !matchStarted) {
                List<string> unreadyPlayers = new List<string>();

                foreach (var key in playerReadyStatus.Keys) {
                    if (playerReadyStatus[key] == false) {
                        unreadyPlayers.Add(playerData[key].PlayerName);
                    }
                }
                if (unreadyPlayers.Count > 0) {
                    string unreadyPlayerList = string.Join(", ", unreadyPlayers);
                    string minimumReadyRequiredMessage = isMatchSetup ? "" : $"[Minimum ready players required: {ChatColors.Green}{minimumReadyRequired}{ChatColors.Default}]";

                    Server.PrintToChatAll($"{chatPrefix} Unready players: {unreadyPlayerList}. Please type .ready to ready up! {minimumReadyRequiredMessage}");
                } else {
                    int countOfReadyPlayers = playerReadyStatus.Count(kv => kv.Value == true);
                    if (isMatchSetup)
                    {
                        Server.PrintToChatAll($"{chatPrefix} Current ready players: {ChatColors.Green}{countOfReadyPlayers}{ChatColors.Default}");
                    }
                    else
                    {
                        Server.PrintToChatAll($"{chatPrefix} Minimum ready players required {ChatColors.Green}{minimumReadyRequired}{ChatColors.Default}, current ready players: {ChatColors.Green}{countOfReadyPlayers}{ChatColors.Default}");
                    }
                }
            }
        }

        private void SendPausedStateMessage() {
            if (isPaused && matchStarted) {
                var pauseTeamName = unpauseData["pauseTeam"];
                if ((string)pauseTeamName == "Admin") {
                    Server.PrintToChatAll($"{chatPrefix} {ChatColors.Green}Admin{ChatColors.Default} has paused the match.");
                } else if ((string)pauseTeamName == "RoundRestore" && !(bool)unpauseData["t"] && !(bool)unpauseData["ct"]) {
                    Server.PrintToChatAll($"{chatPrefix} Match has been paused because of Round Restore. Both teams need to type {ChatColors.Green}.unpause{ChatColors.Default} to unpause the match");
                } else if ((bool)unpauseData["t"] && !(bool)unpauseData["ct"]) {
                    Server.PrintToChatAll($"{chatPrefix} {ChatColors.Green}{reverseTeamSides["TERRORIST"].teamName}{ChatColors.Default} wants to unpause the match. {ChatColors.Green}{reverseTeamSides["CT"].teamName}{ChatColors.Default}, please write !unpause to confirm.");
                } else if (!(bool)unpauseData["t"] && (bool)unpauseData["ct"]) {
                    Server.PrintToChatAll($"{chatPrefix} {ChatColors.Green}{reverseTeamSides["CT"].teamName}{ChatColors.Default} wants to unpause the match. {ChatColors.Green}{reverseTeamSides["TERRORIST"].teamName}{ChatColors.Default}, please write !unpause to confirm.");
                } else if (!(bool)unpauseData["t"] && !(bool)unpauseData["ct"]) {
                    Server.PrintToChatAll($"{chatPrefix} {ChatColors.Green}{pauseTeamName}{ChatColors.Default} has paused the match. Type .unpause to unpause the match");
                }
            }
        }

        private void ExecWarmupCfg() {
            var absolutePath = Path.Join(Server.GameDirectory + "/csgo/cfg", warmupCfgPath);

            if (File.Exists(Path.Join(Server.GameDirectory + "/csgo/cfg", warmupCfgPath))) {
                Log($"[StartWarmup] Starting warmup! Executing Warmup CFG from {warmupCfgPath}");
                Server.ExecuteCommand($"exec {warmupCfgPath}");
            } else {
                Log($"[StartWarmup] Starting warmup! Warmup CFG not found in {absolutePath}, using default CFG!");
                Server.ExecuteCommand("bot_kick;bot_quota 0;mp_autokick 0;mp_autoteambalance 0;mp_buy_anywhere 0;mp_buytime 15;mp_death_drop_gun 0;mp_free_armor 0;mp_ignore_round_win_conditions 0;mp_limitteams 0;mp_radar_showall 0;mp_respawn_on_death_ct 0;mp_respawn_on_death_t 0;mp_solid_teammates 0;mp_spectators_max 20;mp_maxmoney 16000;mp_startmoney 16000;mp_timelimit 0;sv_alltalk 0;sv_auto_full_alltalk_during_warmup_half_end 0;sv_coaching_enabled 1;sv_competitive_official_5v5 1;sv_deadtalk 1;sv_full_alltalk 0;sv_grenade_trajectory 0;sv_hibernate_when_empty 0;mp_weapons_allow_typecount -1;sv_infinite_ammo 0;sv_showimpacts 0;sv_voiceenable 1;sm_cvar sv_mute_players_with_social_penalties 0;sv_mute_players_with_social_penalties 0;tv_relayvoice 1;sv_cheats 0;mp_ct_default_melee weapon_knife;mp_ct_default_secondary weapon_hkp2000;mp_ct_default_primary \"\";mp_t_default_melee weapon_knife;mp_t_default_secondary weapon_glock;mp_t_default_primary;mp_maxrounds 24;mp_warmup_start;mp_warmup_pausetimer 1;mp_warmuptime 9999;cash_team_bonus_shorthanded 0;cash_team_loser_bonus_shorthanded 0;");
            }
        }

        private void StartWarmup() {
            unreadyPlayerMessageTimer?.Kill();
            unreadyPlayerMessageTimer = null;
            if (unreadyPlayerMessageTimer == null) {
                unreadyPlayerMessageTimer = AddTimer(chatTimerDelay, SendUnreadyPlayersMessage, TimerFlags.REPEAT);
            }
            isWarmup = true;
            ExecWarmupCfg();
        }

        private void StartKnifeRound() {
            // Kills unready players message timer
            if (unreadyPlayerMessageTimer != null) {
                unreadyPlayerMessageTimer.Kill();
                unreadyPlayerMessageTimer = null;
            }
            
            // Setting match phases bools
            isKnifeRound = true;
            matchStarted = true;
            readyAvailable = false;
            isWarmup = false;

            var absolutePath = Path.Join(Server.GameDirectory + "/csgo/cfg", knifeCfgPath);

            if (File.Exists(Path.Join(Server.GameDirectory + "/csgo/cfg", knifeCfgPath))) {
                Log($"[StartKnifeRound] Starting Knife! Executing Knife CFG from {knifeCfgPath}");
                Server.ExecuteCommand($"exec {knifeCfgPath}");
                Server.ExecuteCommand("mp_restartgame 1;mp_warmup_end;");
            } else {
                Log($"[StartKnifeRound] Starting Knife! Knife CFG not found in {absolutePath}, using default CFG!");
                Server.ExecuteCommand("mp_ct_default_secondary \"\";mp_free_armor 1;mp_freezetime 10;mp_give_player_c4 0;mp_maxmoney 0;mp_respawn_immunitytime 0;mp_respawn_on_death_ct 0;mp_respawn_on_death_t 0;mp_roundtime 1.92;mp_roundtime_defuse 1.92;mp_roundtime_hostage 1.92;mp_t_default_secondary \"\";mp_round_restart_delay 3;mp_team_intro_time 0;mp_restartgame 1;mp_warmup_end;");
            }
            
            Server.PrintToChatAll($"{chatPrefix} {ChatColors.Green}KNIFE!");
            Server.PrintToChatAll($"{chatPrefix} {ChatColors.Green}KNIFE!");
            Server.PrintToChatAll($"{chatPrefix} {ChatColors.Green}KNIFE!");
        }

        private void SendSideSelectionMessage() {
            if (isSideSelectionPhase) {
                Server.PrintToChatAll($"{chatPrefix} {ChatColors.Green}{knifeWinnerName}{ChatColors.Default} Won the knife. Waiting for them to type {ChatColors.Green}.stay{ChatColors.Default} or {ChatColors.Green}.switch{ChatColors.Default}");
            }
        }

        private void StartAfterKnifeWarmup() {
            isWarmup = true;
            ExecWarmupCfg();
            knifeWinnerName = knifeWinner == 3 ? reverseTeamSides["CT"].teamName : reverseTeamSides["TERRORIST"].teamName;
            ShowDamageInfo();
            Server.PrintToChatAll($"{chatPrefix} {ChatColors.Green}{knifeWinnerName}{ChatColors.Default} Won the knife. Waiting for them to type {ChatColors.Green}.stay{ChatColors.Default} or {ChatColors.Green}.switch{ChatColors.Default}");
            if (sideSelectionMessageTimer == null) {
                sideSelectionMessageTimer = AddTimer(chatTimerDelay, SendSideSelectionMessage, TimerFlags.REPEAT);
            }
        }

        private void StartLive() {

            // Setting match phases bools
            isWarmup = false;
            isSideSelectionPhase = false;
            matchStarted = true;
            isMatchLive = true;
            readyAvailable = false;
            isKnifeRound = false;

            // Storing 0-0 score backup file as lastBackupFileName, so that .stop functions properly in first round.
            lastBackupFileName = $"matchzy_{liveMatchId}_{matchConfig.CurrentMapNumber}_round00.txt";

            KillPhaseTimers();

            var absolutePath = Path.Join(Server.GameDirectory + "/csgo/cfg", liveCfgPath);

            // We try to find the CFG in the cfg folder, if it is not there then we execute the default CFG.
            if (File.Exists(Path.Join(Server.GameDirectory + "/csgo/cfg", liveCfgPath))) {
                Log($"[StartLive] Starting Live! Executing Live CFG from {liveCfgPath}");
                Server.ExecuteCommand($"exec {liveCfgPath}");
                Server.ExecuteCommand("mp_restartgame 1;mp_warmup_end;");
            } else {
                Log($"[StartLive] Starting Live! Live CFG not found in {absolutePath}, using default CFG!");
                Server.ExecuteCommand("ammo_grenade_limit_default 1;ammo_grenade_limit_flashbang 2;ammo_grenade_limit_total 4;bot_quota 0;cash_player_bomb_defused 300;cash_player_bomb_planted 300;cash_player_damage_hostage -30;cash_player_interact_with_hostage 300;cash_player_killed_enemy_default 300;cash_player_killed_enemy_factor 1;cash_player_killed_hostage -1000;cash_player_killed_teammate -300;cash_player_rescued_hostage 1000;cash_team_elimination_bomb_map 3250;cash_team_elimination_hostage_map_ct 3000;cash_team_elimination_hostage_map_t 3000;cash_team_hostage_alive 0;cash_team_hostage_interaction 600;cash_team_loser_bonus 1400;cash_team_loser_bonus_consecutive_rounds 500;cash_team_planted_bomb_but_defused 800;cash_team_rescued_hostage 600;cash_team_terrorist_win_bomb 3500;cash_team_win_by_defusing_bomb 3500;");
                Server.ExecuteCommand("cash_team_win_by_hostage_rescue 2900;cash_team_win_by_time_running_out_bomb 3250;cash_team_win_by_time_running_out_hostage 3250;ff_damage_reduction_bullets 0.33;ff_damage_reduction_grenade 0.85;ff_damage_reduction_grenade_self 1;ff_damage_reduction_other 0.4;mp_afterroundmoney 0;mp_autokick 0;mp_autoteambalance 0;mp_backup_restore_load_autopause 1;mp_backup_round_auto 1;mp_buy_anywhere 0;mp_buy_during_immunity 0;mp_buytime 20;mp_c4timer 40;mp_ct_default_melee weapon_knife;mp_ct_default_primary \"\";mp_ct_default_secondary weapon_hkp2000;mp_death_drop_defuser 1;mp_death_drop_grenade 2;mp_death_drop_gun 1;mp_defuser_allocation 0;mp_display_kill_assists 1;mp_endmatch_votenextmap 0;mp_forcecamera 1;mp_free_armor 0;mp_freezetime 18;mp_friendlyfire 1;mp_give_player_c4 1;mp_halftime 1;mp_halftime_duration 15;mp_halftime_pausetimer 0;mp_ignore_round_win_conditions 0;mp_limitteams 0;mp_match_can_clinch 1;mp_match_end_restart 0;mp_maxmoney 16000;mp_maxrounds 24;mp_molotovusedelay 0;mp_overtime_enable 1;mp_overtime_halftime_pausetimer 0;mp_overtime_maxrounds 6;mp_overtime_startmoney 10000;mp_playercashawards 1;mp_randomspawn 0;mp_respawn_immunitytime 0;mp_respawn_on_death_ct 0;mp_respawn_on_death_t 0;mp_round_restart_delay 5;mp_roundtime 1.92;mp_roundtime_defuse 1.92;mp_roundtime_hostage 1.92;mp_solid_teammates 1;mp_starting_losses 1;mp_startmoney 800;mp_t_default_melee weapon_knife;mp_t_default_primary \"\";mp_t_default_secondary weapon_glock;mp_teamcashawards 1;mp_timelimit 0;mp_weapons_allow_map_placed 1;mp_weapons_allow_zeus 1;mp_weapons_glow_on_ground 0;mp_win_panel_display_time 3;occlusion_test_async 0;spec_freeze_deathanim_time 0;spec_freeze_panel_extended_time 0;spec_freeze_time 2;spec_freeze_time_lock 2;spec_replay_enable 0;sv_allow_votes 1;sv_auto_full_alltalk_during_warmup_half_end 0;sv_coaching_enabled 1;sv_competitive_official_5v5 1;sv_damage_print_enable 0;sv_deadtalk 1;sv_hibernate_postgame_delay 300;sv_holiday_mode 0;sv_ignoregrenaderadio 0;sv_infinite_ammo 0;sv_occlude_players 1;sv_talk_enemy_dead 0;sv_talk_enemy_living 0;sv_voiceenable 1;tv_relayvoice 1;mp_team_timeout_max 4;mp_team_timeout_time 30;sv_vote_command_delay 0;cash_team_bonus_shorthanded 0;cash_team_loser_bonus_shorthanded 0;mp_spectators_max 20;mp_team_intro_time 0;mp_restartgame 3;mp_warmup_end;");
            }
            
            // This is to reload the map once it is over so that all flags are reset accordingly
            Server.ExecuteCommand("mp_match_end_restart true");
            
            Server.PrintToChatAll($"{chatPrefix} {ChatColors.Green}LIVE!");
            Server.PrintToChatAll($"{chatPrefix} {ChatColors.Green}LIVE!");
            Server.PrintToChatAll($"{chatPrefix} {ChatColors.Green}LIVE!");

            // Adding timer here to make sure that CFG execution is completed till then
            AddTimer(1, () => {
                if (isPlayOutEnabled) {
                    Server.ExecuteCommand("mp_match_can_clinch false");
                } else {
                    Server.ExecuteCommand("mp_match_can_clinch true");
                }
                ExecuteChangedConvars();
            });

            var goingLiveEvent = new GoingLiveEvent
            {
                MatchId = liveMatchId.ToString(),
                MapNumber = matchConfig.CurrentMapNumber,
            };

            Task.Run(async () => {
                await SendEventAsync(goingLiveEvent);
            });
        }

        private void KillPhaseTimers() {
            if (unreadyPlayerMessageTimer != null) {
                unreadyPlayerMessageTimer.Kill();
            }
            if (sideSelectionMessageTimer != null) {
                sideSelectionMessageTimer.Kill();
            }
            if (pausedStateTimer != null) {
                pausedStateTimer.Kill();
            }
            unreadyPlayerMessageTimer = null;
            sideSelectionMessageTimer = null;
            pausedStateTimer = null;
        }


        private (int alivePlayers, int totalHealth) GetAlivePlayers(int team) {
            int count = 0;
            int totalHealth = 0;
            foreach (var key in playerData.Keys) {
                if (team == 2 && reverseTeamSides["TERRORIST"].coach == playerData[key]) continue;
                if (team == 3 && reverseTeamSides["CT"].coach == playerData[key]) continue;
                if (playerData[key].PlayerPawn == null) continue;
                if (!playerData[key].PlayerPawn.IsValid) continue;
                if (playerData[key].TeamNum == team) {
                    if (playerData[key].PlayerPawn.Value.Health > 0) count++;
                    totalHealth += playerData[key].PlayerPawn.Value.Health;
                }
            }
            return (count, totalHealth);
        }

        private void ResetMatch(bool warmupCfgRequired = true) 
        {
            try
            {
                // We stop demo recording if a live match was restarted
                if (matchStarted && isDemoRecording) {
                    Server.ExecuteCommand($"tv_stoprecord");
                }
                // Reset match data
                matchStarted = false;
                readyAvailable = true;
                isPaused = false;
                isMatchSetup = false;

                isWarmup = true;
                isKnifeRound = false;
                isSideSelectionPhase = false;
                isMatchLive = false;    
                liveMatchId = -1; 
                isPractice = false;
                isVeto = false;
                isPreVeto = false;

                lastBackupFileName = "";

                // Unready all players
                foreach (var key in playerReadyStatus.Keys) {
                    playerReadyStatus[key] = false;
                }

                HandleClanTags();

                // Reset unpause data
                Dictionary<string, object> unpauseData = new()
                {
                    { "ct", false },
                    { "t", false },
                    { "pauseTeam", "" }
                };

                // Reset stop data
                stopData["ct"] = false;
                stopData["t"] = false;

                // Reset owned bots data
                pracUsedBots = new Dictionary<int, Dictionary<string, object>>();
                UnpauseMatch();
                
                matchzyTeam1.teamName = "COUNTER-TERRORISTS";
                matchzyTeam2.teamName = "TERRORISTS";

                matchzyTeam1.teamPlayers = null;
                matchzyTeam2.teamPlayers = null;

                if (matchzyTeam1.coach != null) matchzyTeam1.coach.Clan = "";
                if (matchzyTeam2.coach != null) matchzyTeam2.coach.Clan = "";

                matchzyTeam1.coach = null;
                matchzyTeam2.coach = null;

                matchzyTeam1.seriesScore = 0;
                matchzyTeam2.seriesScore = 0;

                Server.ExecuteCommand($"mp_teamname_1 {matchzyTeam1.teamName}");
                Server.ExecuteCommand($"mp_teamname_2 {matchzyTeam2.teamName}");

                teamSides[matchzyTeam1] = "CT";
                teamSides[matchzyTeam2] = "TERRORIST";
                reverseTeamSides["CT"] = matchzyTeam1;
                reverseTeamSides["TERRORIST"] = matchzyTeam2;

                matchConfig = new();

                KillPhaseTimers();
                UpdatePlayersMap();
                if (warmupCfgRequired) {
                    StartWarmup();
                } else {
                    // Since we should be already in warmup phase by this point, we are juts setting up the SendUnreadyPlayersMessage timer
                    unreadyPlayerMessageTimer?.Kill();
                    unreadyPlayerMessageTimer = null;
                    if (unreadyPlayerMessageTimer == null) {
                        unreadyPlayerMessageTimer = AddTimer(chatTimerDelay, SendUnreadyPlayersMessage, TimerFlags.REPEAT);
                    }
                }
            }
            catch (Exception ex)
            {
                Log($"[ResetMatch - FATAL] [ERROR]: {ex.Message}");
            } 
        }

        private void UpdatePlayersMap() {
            try
            {
                var playerEntities = Utilities.FindAllEntitiesByDesignerName<CCSPlayerController>("cs_player_controller");
                Log($"[UpdatePlayersMap] CCSPlayerController count: {playerEntities.Count<CCSPlayerController>()} matchModeOnly: {matchModeOnly}");
                connectedPlayers = 0;

                // Clear the playerData dictionary by creating a new instance to add fresh data.
                playerData = new Dictionary<int, CCSPlayerController>();
                foreach (var player in playerEntities) {
                    if (player == null) continue;
                    if (!player.IsValid || player.IsBot || player.IsHLTV) continue;

                    if (isMatchSetup || matchModeOnly) {
                        CsTeam team = GetPlayerTeam(player);
                        if (team == CsTeam.None) {
                            Server.ExecuteCommand($"kickid {(ushort)player.UserId}");
                            continue;
                        }
                    }

                    // A player controller still exists after a player disconnects
                    // Hence checking whether the player is actually in the server or not
                    if (player.Connected != PlayerConnectedState.PlayerConnected) continue;

                    if (player.UserId.HasValue) {

                        // Updating playerData and playerReadyStatus
                        playerData[player.UserId.Value] = player;

                        // Adding missing player in playerReadyStatus
                        if (!playerReadyStatus.ContainsKey(player.UserId.Value)) {
                            playerReadyStatus[player.UserId.Value] = false;
                        }
                    }
                    connectedPlayers++;
                }

                // Removing disconnected players from playerReadyStatus
                foreach (var key in playerReadyStatus.Keys.ToList()) {
                    if (!playerData.ContainsKey(key)) {
                        // Key is not present in playerData, so remove it from playerReadyStatus
                        playerReadyStatus.Remove(key);
                    }
                }
                Log($"[UpdatePlayersMap] CCSPlayerController count: {playerEntities.Count<CCSPlayerController>()}, RealPlayersCount: {GetRealPlayersCount()}");
            }
            catch (Exception e)
            {
                Log($"[UpdatePlayersMap FATAL] An error occurred: {e.Message}");
            }
        }

        private void HandleKnifeWinner(EventCsWinPanelRound @event) {
            // Knife Round code referred from Get5, thanks to the Get5 team for their amazing job!
            (int tAlive, int tHealth) = GetAlivePlayers(2);
            (int ctAlive, int ctHealth) = GetAlivePlayers(3);
            Log($"[KNIFE OVER] CT Alive: {ctAlive} with Total Health: {ctHealth}, T Alive: {tAlive} with Total Health: {tHealth}");
            if (ctAlive > tAlive) {
                knifeWinner = 3;
            } else if (tAlive > ctAlive) {
                knifeWinner = 2;
            } else if (ctHealth > tHealth) {
                knifeWinner = 3;
            } else if (tHealth > ctHealth) {
                knifeWinner = 2;
            } else {
                // Choosing a winner randomly
                Random random = new Random();
                knifeWinner = random.Next(2, 4);
            }

            // Below code is working partially (Winner audio plays correctly for knife winner team, but may display round winner incorrectly)
            // Hence we restart the game with StartAfterKnifeWarmup and allow the winning team to choose side

            @event.FunfactToken = "";

            // Commenting these assignments as they were crashing the server.
            // long empty = 0;
            // @event.FunfactPlayer = null;
            // @event.FunfactData1 = empty;
            // @event.FunfactData2 = empty;
            // @event.FunfactData3 = empty;
            int finalEvent = 10;
            if (knifeWinner == 3) {
                finalEvent = 8;
            } else if (knifeWinner == 2) {
                finalEvent = 9;
            }
            Log($"[KNIFE WINNER] Won by: {knifeWinner}, finalEvent: {@event.FinalEvent}, newFinalEvent: {finalEvent}");
            @event.FinalEvent = finalEvent;
        }

        private void HandleMapChangeCommand(CCSPlayerController? player, string mapName) {
            if (player == null) return;
            if (!IsPlayerAdmin(player, "css_map", "@css/map")) {
                SendPlayerNotAdminMessage(player);
                return;
            }

            if (matchStarted) {
                player.PrintToChat($"{chatPrefix} Map cannot be changed once the match is started!");
                return;
            }

            if (long.TryParse(mapName, out _)) { // Check if mapName is a long for workshop map ids
                Server.ExecuteCommand($"host_workshop_map \"{mapName}\"");
            } else if (Server.IsMapValid(mapName)) {
                Server.ExecuteCommand($"changelevel \"{mapName}\"");
            } else {
                player.PrintToChat($"{chatPrefix} Invalid map name!");
            }
        }

        private void HandleReadyRequiredCommand(CCSPlayerController? player, string commandArg) {
            if (!IsPlayerAdmin(player, "css_readyrequired", "@css/config")) {
                SendPlayerNotAdminMessage(player);
                return;
            }
            
            if (!string.IsNullOrWhiteSpace(commandArg)) {
                if (int.TryParse(commandArg, out int readyRequired) && readyRequired >= 0 && readyRequired <= 32) {
                    minimumReadyRequired = readyRequired;
                    string minimumReadyRequiredFormatted = (player == null) ? $"{minimumReadyRequired}" : $"{ChatColors.Green}{minimumReadyRequired}{ChatColors.Default}";
                    ReplyToUserCommand(player, $"Minimum ready players required to start the match are now set to: {minimumReadyRequiredFormatted}");
                    CheckLiveRequired();
                }
                else {
                    ReplyToUserCommand(player, $"Invalid value for readyrequired. Please specify a valid non-negative number. Usage: !readyrequired <number_of_ready_players_required>");
                }
            }
            else {
                string minimumReadyRequiredFormatted = (player == null) ? $"{minimumReadyRequired}" : $"{ChatColors.Green}{minimumReadyRequired}{ChatColors.Default}";
                ReplyToUserCommand(player, $"Current Ready Required: {minimumReadyRequiredFormatted} .Usage: !readyrequired <number_of_ready_players_required>");
            }
        }

        private void CheckLiveRequired() {
            if (!readyAvailable || matchStarted) return;

            // Todo: Implement a same ready system for both pug and match
            int countOfReadyPlayers = playerReadyStatus.Count(kv => kv.Value == true);
            bool liveRequired = false;
            if (isMatchSetup) {
                if (IsTeamsReady() && IsSpectatorsReady()) {
                    liveRequired = true;
                }
            }
            else if (minimumReadyRequired == 0) {
                if (countOfReadyPlayers >= connectedPlayers && connectedPlayers > 0) {
                    liveRequired = true;
                }
            } else if (countOfReadyPlayers >= minimumReadyRequired) {
                liveRequired = true;
            }
            if (liveRequired) {
                HandleMatchStart();
            }
        }

        private void HandleMatchStart() {
            isPractice = false;

            // If default names, we pick a player and use their name as their team name
            if (matchzyTeam1.teamName == "COUNTER-TERRORISTS") {
                // matchzyTeam1.teamName = teamName;
                teamSides[matchzyTeam1] = "CT";
                reverseTeamSides["CT"] = matchzyTeam1;
                foreach (var key in playerData.Keys) {
                    if (playerData[key].TeamNum == 3) {
                        matchzyTeam1.teamName = "team_" + RemoveSpecialCharacters(playerData[key].PlayerName.Replace(" ", "_"));
                        if (matchzyTeam1.coach != null) matchzyTeam1.coach.Clan = $"[{matchzyTeam1.teamName} COACH]";
                        break;
                    }
                }
                // Server.ExecuteCommand($"mp_teamname_1 {matchzyTeam1.teamName}");
            }

            if (matchzyTeam2.teamName == "TERRORISTS") {
                // matchzyTeam2.teamName = teamName;
                teamSides[matchzyTeam2] = "TERRORIST";
                reverseTeamSides["TERRORIST"] = matchzyTeam2;
                foreach (var key in playerData.Keys) {
                    if (playerData[key].TeamNum == 2) {
                        matchzyTeam2.teamName = "team_" + RemoveSpecialCharacters(playerData[key].PlayerName.Replace(" ", "_"));
                        if (matchzyTeam2.coach != null) matchzyTeam2.coach.Clan = $"[{matchzyTeam2.teamName} COACH]";
                        break;
                    }
                }
                // Server.ExecuteCommand($"mp_teamname_2 {matchzyTeam2.teamName}");
            }

            Server.ExecuteCommand($"mp_teamname_1 {reverseTeamSides["CT"].teamName}");
            Server.ExecuteCommand($"mp_teamname_2 {reverseTeamSides["TERRORIST"].teamName}");

            HandleClanTags();

            string seriesType = "BO" + matchConfig.NumMaps.ToString();
            liveMatchId = database.InitMatch(matchzyTeam1.teamName, matchzyTeam2.teamName, "-" , isMatchSetup, liveMatchId, matchConfig.CurrentMapNumber, seriesType);
            SetupRoundBackupFile();
            StartDemoRecording();

            if (isPreVeto)
            {
                CreateVeto();
            }
            else if (isKnifeRequired) 
            {
                StartKnifeRound();  
            } 
            else 
            {
                StartLive();
            }
            Server.PrintToChatAll($"{chatPrefix} {ChatColors.Green}MatchZy{ChatColors.Default} Plugin by {ChatColors.Green}WD-{ChatColors.Default}");
        }

        public void HandleClanTags() {
            // Currently it is not possible to keep updating player tags while in warmup without restarting the match
            // Hence returning from here until we find a proper solution
            return;
            
            if (readyAvailable && !matchStarted) {
                foreach (var key in playerData.Keys) {
                    if (playerReadyStatus[key]) {
                        playerData[key].Clan = "[Ready]";
                    } else {
                        playerData[key].Clan = "[Unready]";
                    }
                    Server.PrintToChatAll($"PlayerName: {playerData[key].PlayerName} Clan: {playerData[key].Clan}");
                }
            } else if (matchStarted) {
                foreach (var key in playerData.Keys) {
                    if (playerData[key].TeamNum == 2) {
                        playerData[key].Clan = reverseTeamSides["TERRORIST"].teamTag;
                    } else if (playerData[key].TeamNum == 3) {
                        playerData[key].Clan = reverseTeamSides["CT"].teamTag;
                    }
                    Server.PrintToChatAll($"PlayerName: {playerData[key].PlayerName} Clan: {playerData[key].Clan}");
                }
            }
        }

        private void HandleMatchEnd() {
            if (!isMatchLive) return;

            // This ensures that the mp_match_restart_delay is not shorter than what is required for the GOTV recording to finish.
            // Ref: Get5
            int restartDelay = ConVar.Find("mp_match_restart_delay")!.GetPrimitiveValue<int>();
            int tvDelay = GetTvDelay();
            int requiredDelay = tvDelay + 15;
            int tvFlushDelay = requiredDelay;
            if (tvDelay > 0.0) {
                requiredDelay += 10;
            }
            if (requiredDelay > restartDelay) {
                Log($"Extended mp_match_restart_delay from {restartDelay} to {requiredDelay} to ensure GOTV broadcast can finish.");
                ConVar.Find("mp_match_restart_delay")!.SetValue(requiredDelay);
                restartDelay = requiredDelay;
            }
            int currentMapNumber = matchConfig.CurrentMapNumber;
            Log($"[HandleMatchEnd] MAP ENDED, isMatchSetup: {isMatchSetup} matchid: {liveMatchId} currentMapNumber: {currentMapNumber} tvFlushDelay: {tvFlushDelay}");

            StopDemoRecording(tvFlushDelay - 0.5f, activeDemoFile + ".dem", liveMatchId, currentMapNumber);

            string winnerName = GetMatchWinnerName();
            (int t1score, int t2score) = GetTeamsScore();
            int team1SeriesScore = matchzyTeam1.seriesScore;
            int team2SeriesScore = matchzyTeam2.seriesScore;

            string statsPath = Server.GameDirectory + "/csgo/MatchZy_Stats/" + liveMatchId.ToString();

            var mapResultEvent = new MapResultEvent
            {
                MatchId = liveMatchId.ToString(),
                MapNumber = currentMapNumber,
                Winner = new Winner(t1score > t2score && reverseTeamSides["CT"] == matchzyTeam1 ? "3" : "2", team1SeriesScore > team2SeriesScore ? "team1" : "team2"),
                StatsTeam1 = new MatchZyStatsTeam(matchzyTeam1.id, matchzyTeam1.teamName, team1SeriesScore, t1score, 0, 0, new List<StatsPlayer>()),
                StatsTeam2 = new MatchZyStatsTeam(matchzyTeam2.id, matchzyTeam2.teamName, team2SeriesScore, t2score, 0, 0, new List<StatsPlayer>())
            };

            Task.Run(async () => {
                await SendEventAsync(mapResultEvent);
                await database.SetMapEndData(liveMatchId, currentMapNumber, winnerName, t1score, t2score, team1SeriesScore, team2SeriesScore);
                await database.WritePlayerStatsToCsv(statsPath, liveMatchId, currentMapNumber);
            });

            // If a match is not setup, it was supposed to be a pug/scrim with 1 map
            // Hence we reset the match once it is over
            // Todo: Support BO3/BO5 in pugs as well
            if (!isMatchSetup)
            {
                EndSeries(winnerName, restartDelay - 1);
                return;
            }

            int remainingMaps = matchConfig.NumMaps - matchzyTeam1.seriesScore - matchzyTeam2.seriesScore;
            Log($"[HandleMatchEnd] MATCH ENDED, remainingMaps: {remainingMaps}, NumMaps: {matchConfig.NumMaps}, Team1SeriesScore: {matchzyTeam1.seriesScore}, Team2SeriesScore: {matchzyTeam2.seriesScore}");
            if (matchConfig.SeriesCanClinch) {
                int mapsToWinSeries = (matchConfig.NumMaps / 2) + 1;
                if (matchzyTeam1.seriesScore == mapsToWinSeries) {
                    EndSeries(winnerName, restartDelay - 1);
                    return;
                } else if (matchzyTeam2.seriesScore == mapsToWinSeries) {
                    EndSeries(winnerName, restartDelay - 1);
                    return;      
                }
            } else if (remainingMaps <= 0) {
                EndSeries(winnerName, restartDelay - 1);
                return;
            }
            if (matchzyTeam1.seriesScore > matchzyTeam2.seriesScore) {
                Server.PrintToChatAll($"{chatPrefix} {ChatColors.Green}{matchzyTeam1.teamName}{ChatColors.Default} is winning the series {ChatColors.Green}{matchzyTeam1.seriesScore}-{matchzyTeam2.seriesScore}{ChatColors.Default}");

            } else if (matchzyTeam2.seriesScore > matchzyTeam1.seriesScore) {
                Server.PrintToChatAll($"{chatPrefix} {ChatColors.Green}{matchzyTeam2.teamName}{ChatColors.Default} is winning the series {ChatColors.Green}{matchzyTeam2.seriesScore}-{matchzyTeam1.seriesScore}{ChatColors.Default}");

            } else {
                Server.PrintToChatAll($"{chatPrefix} The series is tied at {ChatColors.Green}{matchzyTeam1.seriesScore}-{matchzyTeam2.seriesScore}{ChatColors.Default}");
            }
            matchConfig.CurrentMapNumber += 1;
            string nextMap = matchConfig.Maplist[matchConfig.CurrentMapNumber];

            if (isPaused) 
                UnpauseMatch();

            stopData["ct"] = false;
            stopData["t"] = false;

            KillPhaseTimers();

            AddTimer(restartDelay - 4, () => {
                if (!isMatchSetup) return;
                ChangeMap(nextMap, 3.0f);
                matchStarted = false;
                readyAvailable = true;
                isPaused = false;

                isWarmup = true;
                isKnifeRound = false;
                isSideSelectionPhase = false;
                isMatchLive = false;    
                isPractice = false;
                StartWarmup();
                SetMapSides();
            });
        }

        private void ChangeMap(string mapName, float delay)
        {
            Log($"[ChangeMap] Changing map to {mapName} with delay {delay}");
            AddTimer(delay, () => {
                if (long.TryParse(mapName, out _)) {
                    Server.ExecuteCommand($"host_workshop_map \"{mapName}\"");
                } else if (Server.IsMapValid(mapName)) {
                    Server.ExecuteCommand($"changelevel \"{mapName}\"");
                }
            });
        }

        private void ChangeMapOnMatchEnd() {
            ResetMatch();
            string mapName = Server.MapName;
            if (long.TryParse(mapName, out _)) {
                Server.ExecuteCommand($"host_workshop_map \"{mapName}\"");
            } else if (Server.IsMapValid(mapName)) {
                Server.ExecuteCommand($"changelevel \"{mapName}\"");
            }
        }

        private string GetMatchWinnerName() {
            (int t1score, int t2score) = GetTeamsScore();
            if (t1score > t2score) {
                matchzyTeam1.seriesScore++;
                return matchzyTeam1.teamName;
            } else if (t2score > t1score) {
                matchzyTeam2.seriesScore++;
                return matchzyTeam2.teamName;
            } else {
                return "Draw";
            }
        }

        private (int t1score, int t2score) GetTeamsScore()
        {
            var teamEntities = Utilities.FindAllEntitiesByDesignerName<CCSTeam>("cs_team_manager");
            int t1score = 0;
            int t2score = 0;
            foreach (var team in teamEntities)
            {

                if (team.Teamname == teamSides[matchzyTeam1])
                {
                    t1score = team.Score;
                }
                else if (team.Teamname == teamSides[matchzyTeam2])
                {
                    t2score = team.Score;
                }
            }
            return (t1score, t2score);
        }

        public void HandlePostRoundStartEvent(EventRoundStart @event) {
            HandleCoaches();
            CreateMatchZyRoundDataBackup();
            InitPlayerDamageInfo();
        }

        public void HandlePostRoundFreezeEndEvent(EventRoundFreezeEnd @event)
        {
            List<CCSPlayerController?> coaches = new List<CCSPlayerController?>
            {
                matchzyTeam1.coach,
                matchzyTeam2.coach
            };

            foreach (var coach in coaches) 
            {
                if (coach == null) continue;
                AddTimer(1.0f, () => HandleCoachTeam(coach));
            }
        }

        private void HandleCoachTeam(CCSPlayerController playerController, bool isFreezeTime = false)
        {
            CsTeam oldTeam = CsTeam.Spectator;
            if (matchzyTeam1.coach == playerController) {
                if (teamSides[matchzyTeam1] == "CT") {
                    oldTeam = CsTeam.CounterTerrorist;
                } else if (teamSides[matchzyTeam1] == "TERRORIST") {
                    oldTeam = CsTeam.Terrorist;
                }
            }
            if (matchzyTeam2.coach == playerController) {
                if (teamSides[matchzyTeam2] == "CT") {
                    oldTeam = CsTeam.CounterTerrorist;
                } else if (teamSides[matchzyTeam2] == "TERRORIST") {
                    oldTeam = CsTeam.Terrorist;
                }
            }
            if (!(isFreezeTime && playerController.TeamNum == (int)oldTeam)) {
                playerController.ChangeTeam(CsTeam.Spectator);
                playerController.ChangeTeam(oldTeam);
            }
            if (playerController.InGameMoneyServices != null) playerController.InGameMoneyServices.Account = 0;
        }

        private void HandlePostRoundEndEvent(EventRoundEnd @event) {
            try {
                if (isMatchLive) {
                    (int t1score, int t2score) = GetTeamsScore();
                    Server.PrintToChatAll($"{chatPrefix} {ChatColors.Green}{matchzyTeam1.teamName} [{t1score} - {t2score}] {matchzyTeam2.teamName}");

                    ShowDamageInfo();

                    (Dictionary<ulong, Dictionary<string, object>> playerStatsDictionary, List<StatsPlayer> playerStatsListTeam1, List<StatsPlayer> playerStatsListTeam2) = GetPlayerStatsDict();

                    int currentMapNumber = matchConfig.CurrentMapNumber;
                    long matchId = liveMatchId;
                    int ctTeamNum = reverseTeamSides["CT"] == matchzyTeam1 ? 1 : 2;
                    int tTeamNum = reverseTeamSides["TERRORIST"] == matchzyTeam1 ? 1 : 2;
                    Winner winner  = new(@event.Winner == 3 ? ctTeamNum.ToString() : tTeamNum.ToString(), t1score > t2score ? "team1" : "team2" );

                    var roundEndEvent = new MatchZyRoundEndedEvent
                    {
                        MatchId = liveMatchId.ToString(),
                        MapNumber = matchConfig.CurrentMapNumber,
                        RoundNumber = t1score + t2score,
                        Reason = @event.Reason,
                        RoundTime = 0,
                        Winner = winner,
                        StatsTeam1 = new MatchZyStatsTeam(matchzyTeam1.id, matchzyTeam1.teamName, 0, t1score, 0, 0, playerStatsListTeam1),
                        StatsTeam2 = new MatchZyStatsTeam(matchzyTeam2.id, matchzyTeam2.teamName, 0, t2score, 0, 0, playerStatsListTeam2),
                    };

                    Task.Run(async () => {
                        await SendEventAsync(roundEndEvent);
                        await database.UpdatePlayerStatsAsync(matchId, currentMapNumber, playerStatsDictionary);
                        await database.UpdateMapStatsAsync(matchId, currentMapNumber, t1score, t2score);
                    });

                    string round = (t1score + t2score).ToString("D2");
                    lastBackupFileName = $"matchzy_{liveMatchId}_{matchConfig.CurrentMapNumber}_round{round}.txt";
                    Log($"[HandlePostRoundEndEvent] Setting lastBackupFileName to {lastBackupFileName}");

                    // One of the team did not use .stop command hence display the proper message after the round has ended.
                    if (stopData["ct"] && !stopData["t"]) {
                        Server.PrintToChatAll($"{chatPrefix} The round restore request by {ChatColors.Green}{reverseTeamSides["CT"].teamName}{ChatColors.Default} was cancelled as the round ended");
                    } else if (!stopData["ct"] && stopData["t"]) {
                        Server.PrintToChatAll($"{chatPrefix} The round restore request by {ChatColors.Green}{reverseTeamSides["TERRORIST"].teamName}{ChatColors.Default} was cancelled as the round ended");
                    }

                    // Invalidate .stop requests after a round is completed.
                    stopData["ct"] = false;
                    stopData["t"] = false;

                    bool swapRequired = IsTeamSwapRequired();

                    // If isRoundRestoring is true, sides will be swapped from round restore if required!
                    if (swapRequired && !isRoundRestoring) {
                        SwapSidesInTeamData(false);
                    }

                    isRoundRestoring = false;
                }
            }
            catch (Exception e)
            {
                Log($"[HandlePostRoundEndEvent FATAL] An error occurred: {e.Message}");
            }
        }

        public bool IsTeamSwapRequired() {
            // Handling OTs and side swaps (Referred from Get5)
            var gameRules = Utilities.FindAllEntitiesByDesignerName<CCSGameRulesProxy>("cs_gamerules").First().GameRules!;
            int roundsPlayed = gameRules.TotalRoundsPlayed;

            int roundsPerHalf = ConVar.Find("mp_maxrounds")!.GetPrimitiveValue<int>() / 2;
            int roundsPerOTHalf = ConVar.Find("mp_overtime_maxrounds")!.GetPrimitiveValue<int>() / 2;

            bool halftimeEnabled = ConVar.Find("mp_halftime")!.GetPrimitiveValue<bool>();

            if (halftimeEnabled) {
                if (roundsPlayed == roundsPerHalf) {
                    return true;
                }
                // Now in OT.
                if (roundsPlayed >= 2 * roundsPerHalf) {
                    int otround = roundsPlayed - 2 * roundsPerHalf;  // round 33 -> round 3, etc.
                    // Do side swaps at OT halves (rounds 3, 9, ...)
                    if ((otround + roundsPerOTHalf) % (2 * roundsPerOTHalf) == 0) {
                        return true;
                    }
                }
            }
            return false;
        }

        private void ReplyToUserCommand(CCSPlayerController? player, string message, bool console = false)
        {
            if (player == null) {
                Server.PrintToConsole($"[MatchZy] {message}");
            } else {
                if (console) {
                    player.PrintToConsole($"[MatchZy] {message}");
                } else {
                    player.PrintToChat($"{chatPrefix} {message}");
                }
            }
        }

        private void PauseMatch(CCSPlayerController? player, CommandInfo? command) {
            if (isMatchLive && isPaused) {
                ReplyToUserCommand(player, "Match is already paused!");
                return;
            }
            if (IsHalfTimePhase())
            {
                ReplyToUserCommand(player, "You cannot use this command during halftime.");
                return;
            }
            if (IsPostGamePhase())
            {
                ReplyToUserCommand(player, "You cannot use this command after the game has ended.");
                return;
            }
            if (IsTacticalTimeoutActive())
            {
                ReplyToUserCommand(player, "You cannot use this command when tactical timeout is active.");
                return;
            }
            if (isMatchLive && !isPaused) {

                string pauseTeamName = "Admin";
                unpauseData["pauseTeam"] = "Admin";
                if (player?.TeamNum == 2) {

                    pauseTeamName = reverseTeamSides["TERRORIST"].teamName;
                    unpauseData["pauseTeam"] = reverseTeamSides["TERRORIST"].teamName;
                } else if (player?.TeamNum == 3) {
                    pauseTeamName = reverseTeamSides["CT"].teamName;
                    unpauseData["pauseTeam"] = reverseTeamSides["CT"].teamName;
                } else {
                    return;
                }
                Server.PrintToChatAll($"{chatPrefix} {ChatColors.Green}{pauseTeamName}{ChatColors.Default} has paused the match. Type .unpause to unpause the match");

                SetMatchPausedFlags();
            }
        }

        private void ForcePauseMatch(CCSPlayerController? player, CommandInfo? command)
        {
            if (!matchStarted) return;
            if (!IsPlayerAdmin(player, "css_forcepause", "@css/config")) {
                SendPlayerNotAdminMessage(player);
                return;
            }
            if (isMatchLive && isPaused) {
                ReplyToUserCommand(player, "Match is already paused!");
                return;
            }
            if (IsHalfTimePhase())
            {
                ReplyToUserCommand(player, "You cannot use this command during halftime.");
                return;
            }
            if (IsPostGamePhase())
            {
                ReplyToUserCommand(player, "You cannot use this command after the game has ended.");
                return;
            }
            if (IsTacticalTimeoutActive())
            {
                ReplyToUserCommand(player, "You cannot use this command when tactical timeout is active.");
                return;
            }
            unpauseData["pauseTeam"] = "Admin";
            Server.PrintToChatAll($"{chatPrefix} {ChatColors.Green}Admin{ChatColors.Default} has paused the match.");
            if (player == null) {
                Server.PrintToConsole($"[MatchZy] Admin has paused the match.");
            } 
            SetMatchPausedFlags();
        }

        private void ForceUnpauseMatch(CCSPlayerController? player, CommandInfo? command)
        {
            if (isMatchLive && isPaused) {
                if (!IsPlayerAdmin(player, "css_forceunpause", "@css/config")) {
                    SendPlayerNotAdminMessage(player);
                    return;
                }
                Server.PrintToChatAll($"{chatPrefix} {ChatColors.Green}Admin{ChatColors.Default} has unpaused the match, resuming the match!");
                UnpauseMatch();

                if (player == null) {
                    Server.PrintToConsole("[MatchZy] Admin has unpaused the match, resuming the match!");
                }
            }
        }

        private void UnpauseMatch()
        {
            Server.ExecuteCommand("mp_unpause_match;");
            isPaused = false;
            unpauseData["ct"] = false;
            unpauseData["t"] = false;
            if (!isPaused && pausedStateTimer != null) {
                pausedStateTimer.Kill();
                pausedStateTimer = null;
            }
        }

        private void SetMatchPausedFlags()
        {
            Server.ExecuteCommand("mp_pause_match;");
            isPaused = true;

            if (pausedStateTimer == null) {
                pausedStateTimer = AddTimer(chatTimerDelay, SendPausedStateMessage, TimerFlags.REPEAT);
            }
        }

        private void StartMatchMode() 
        {
            if (matchStarted || (!isPractice && !isSleep)) return;
            ExecUnpracCommands();
            ResetMatch();
            Server.PrintToChatAll($"{chatPrefix} Match mode loaded!");
        }

        private void SendPlayerNotAdminMessage(CCSPlayerController? player) {
            ReplyToUserCommand(player, "You do not have permission to use this command!");
        }

        private string GetColorTreatedString(string message)
        {
            // Adding extra space before args if message starts with a color name
            // This is because colors cannot be applied from 1st character, hence we make first character as an empty space
            if (message.StartsWith('{')) message = " " + message;

            foreach (var field in typeof(ChatColors).GetFields())
            {
                string pattern = $"{{{field.Name}}}";
                string replacement = field.GetValue(null).ToString();

                // Create a case-insensitive regular expression pattern for the color name
                string patternIgnoreCase = Regex.Escape(pattern);
                message = Regex.Replace(message, patternIgnoreCase, replacement, RegexOptions.IgnoreCase);
            }

            return message;
        }

        private void SendAvailableCommandsMessage(CCSPlayerController? player)
        {
            if (isPractice)
            {
                ReplyToUserCommand(player, "Available commands: .spawn, .ctspawn, .tspawn, .bot, .nobots, .god, .clear, .fastforward");
                ReplyToUserCommand(player, ".loadnade <name>, .savenade <name>, .importnade <code> .listnades <optional filter>");
                ReplyToUserCommand(player, ".ct, .t, .spec, .fas");
                return;
            }
            if (readyAvailable)
            {
                ReplyToUserCommand(player, "Available commands: !ready, !unready");
                return;
            }
            if (isSideSelectionPhase)
            {
                ReplyToUserCommand(player, "Available commands: !stay, !switch");
                return;
            }
            if (matchStarted)
            {
                string stopCommandMessage = isStopCommandAvailable ? ", !stop" : "";
                ReplyToUserCommand(player, $"Available commands: !pause, !unpause, !tac, !tech{stopCommandMessage}");
                return;
            }
        }

        public void LoadClientNames()
        {
            string namesFileName = "Match_" + liveMatchId.ToString() + ".ini";
            string namesFilePath = Server.GameDirectory + "/csgo/MatchZyPlayerNames/" + namesFileName;
            string? directoryPath = Path.GetDirectoryName(namesFilePath);
            if (directoryPath != null)
            {
                if (!Directory.Exists(directoryPath))
                {
                    Directory.CreateDirectory(directoryPath);
                }
            }

            StringBuilder sb = new StringBuilder();
            sb.AppendLine("\"Names\"");
            sb.AppendLine("{");

            WriteClientNamesInFile(sb, matchzyTeam1.teamPlayers);
            WriteClientNamesInFile(sb, matchzyTeam2.teamPlayers);
            WriteClientNamesInFile(sb, matchConfig.Spectators);

            sb.AppendLine("}");
            File.WriteAllText(namesFilePath, sb.ToString());
            Server.ExecuteCommand($"sv_load_forced_client_names_file MatchZyPlayerNames/" + namesFileName);
        }

        public void WriteClientNamesInFile(StringBuilder sb, JToken? players)
        {
            if (players == null) return;
            foreach (JProperty player in players)
            {
                string steamId = player.Name;
                string escapedName = player.Value.ToString().Replace("\"", "\\\"").Trim();

                if (string.IsNullOrEmpty(escapedName)) continue;

                sb.AppendLine($"\t\"{steamId}\"\t\t\"{escapedName}\"");
            }
        }

        static bool IsValidUrl(string url)
        {
            if (Uri.TryCreate(url, UriKind.Absolute, out Uri result))
            {
                return result.Scheme == Uri.UriSchemeHttp || result.Scheme == Uri.UriSchemeHttps;
            }
            return false;
        }

        public string GetConvarStringValue(ConVar? cvar)
        {
            try
            {
                if (cvar == null) return "";
                string convarValue = cvar.Type switch
                {
                    ConVarType.Bool => cvar.GetPrimitiveValue<bool>().ToString(),
                    ConVarType.Float32 or ConVarType.Float64 => cvar.GetPrimitiveValue<float>().ToString(),
                    ConVarType.UInt16 => cvar.GetPrimitiveValue<ushort>().ToString(),
                    ConVarType.Int16 => cvar.GetPrimitiveValue<short>().ToString(),
                    ConVarType.UInt32 => cvar.GetPrimitiveValue<uint>().ToString(),
                    ConVarType.Int32 => cvar.GetPrimitiveValue<int>().ToString(),
                    ConVarType.Int64 => cvar.GetPrimitiveValue<long>().ToString(),
                    ConVarType.UInt64 => cvar.GetPrimitiveValue<ulong>().ToString(),
                    ConVarType.String => cvar.StringValue,
                    _ => "",
                };
                return convarValue;
            }
            catch (Exception ex)
            {
                Log($"[GetConvarStringValue - FATAL] Exception occurred: {ex.Message}");
                return "";
            }

        }

        public void SetConvarValue(ConVar? cvar, string value)
        {
            if (cvar == null) return;
            Dictionary<ConVarType, Action<string>> conversionMap = new()
            {
                { ConVarType.Bool, v => cvar.SetValue(int.TryParse(v, out int intValue) && intValue >= 1 || Convert.ToBoolean(v) ) },
                { ConVarType.Float32, v => cvar.SetValue(Convert.ToSingle(v)) },
                { ConVarType.Float64, v => cvar.SetValue(Convert.ToSingle(v)) },
                { ConVarType.UInt16, v => cvar.SetValue(Convert.ToUInt16(v)) },
                { ConVarType.Int16, v => cvar.SetValue(Convert.ToInt16(v)) },
                { ConVarType.UInt32, v => cvar.SetValue(Convert.ToUInt32(v)) },
                { ConVarType.Int32, v => cvar.SetValue(Convert.ToInt32(v)) },
                { ConVarType.Int64, v => cvar.SetValue(Convert.ToInt64(v)) },
                { ConVarType.UInt64, v => cvar.SetValue(Convert.ToUInt64(v)) },
                { ConVarType.String, v => cvar.SetValue(v) },
            };

            if (conversionMap.TryGetValue(cvar.Type, out var conversion))
            {
                try
                {
                    conversion(value);
                }
                catch (Exception ex)
                {
                    Log($"[SetConvarValue - FATAL] Exception occurred: {ex.Message}");
                }
            }
        }

        public void ExecuteChangedConvars()
        {
            foreach (string key in matchConfig.ChangedCvars.Keys)
            {
                string value = matchConfig.ChangedCvars[key];
                Log($"[ExecuteChangedConvars] Execing: {key} \"{value}\"");
                Server.ExecuteCommand($"{key} \"{value}\"");
            }
        }

        public void ResetChangedConvars()
        {
            foreach (string key in matchConfig.OriginalCvars.Keys)
            {
                string value = matchConfig.OriginalCvars[key];
                Log($"[ResetChangedConvars] Execing: {key} \"{value}\"");
                Server.ExecuteCommand($"{key} {value}");
            }
        }

        public int GetGamePhase()
        {
            return Utilities.FindAllEntitiesByDesignerName<CCSGameRulesProxy>("cs_gamerules").First().GameRules!.GamePhase;
        }

        public bool IsHalfTimePhase()
        {
            try
            {
                return GetGamePhase() == 4;
            }
            catch (Exception e)
            {
                Log($"[IsHalfTime FATAL] An error occurred: {e.Message}");
                return false;
            }

        }

        public bool IsPostGamePhase()
        {
            try
            {
                return GetGamePhase() == 5;
            }
            catch (Exception e)
            {
                Log($"[IsPostGamePhase FATAL] An error occurred: {e.Message}");
                return false;
            }

        }

        public bool IsTacticalTimeoutActive()
        {
            var gameRules = Utilities.FindAllEntitiesByDesignerName<CCSGameRulesProxy>("cs_gamerules").First().GameRules!;

            return (gameRules.CTTimeOutActive || gameRules.TerroristTimeOutActive) && gameRules.FreezePeriod;
        }

        public (Dictionary<ulong, Dictionary<string, object>>, List<StatsPlayer>, List<StatsPlayer>)  GetPlayerStatsDict()
        {
            Dictionary<ulong, Dictionary<string, object>> playerStatsDictionary = new Dictionary<ulong, Dictionary<string, object>>();
            List<StatsPlayer> playerStatsListTeam1 = new();
            List<StatsPlayer> playerStatsListTeam2 = new();
            var gameRules = Utilities.FindAllEntitiesByDesignerName<CCSGameRulesProxy>("cs_gamerules").First().GameRules!;
            int roundsPlayed = gameRules.TotalRoundsPlayed;
            try
            {
                foreach (int key in playerData.Keys)
                {
                    CCSPlayerController player = playerData[key];
                    if (!player.IsValid || player.ActionTrackingServices == null) continue;

                    var playerStats = player.ActionTrackingServices.MatchStats;
                    ulong steamid64 = player.SteamID;

                    // Create a nested dictionary to store individual stats for the player
                    Dictionary<string, object> stats = new Dictionary<string, object>
                    {
                        { "PlayerName", player.PlayerName },
                        { "Kills", playerStats.Kills },
                        { "Deaths", playerStats.Deaths },
                        { "Assists", playerStats.Assists },
                        { "Damage", playerStats.Damage },
                        { "Enemy2Ks", playerStats.Enemy2Ks },
                        { "Enemy3Ks", playerStats.Enemy3Ks },
                        { "Enemy4Ks", playerStats.Enemy4Ks },
                        { "Enemy5Ks", playerStats.Enemy5Ks },
                        { "EntryCount", playerStats.EntryCount },
                        { "EntryWins", playerStats.EntryWins },
                        { "1v1Count", playerStats.I1v1Count },
                        { "1v1Wins", playerStats.I1v1Wins },
                        { "1v2Count", playerStats.I1v2Count },
                        { "1v2Wins", playerStats.I1v2Wins },
                        { "UtilityCount", playerStats.Utility_Count },
                        { "UtilitySuccess", playerStats.Utility_Successes },
                        { "UtilityDamage", playerStats.UtilityDamage },
                        { "UtilityEnemies", playerStats.Utility_Enemies },
                        { "FlashCount", playerStats.Flash_Count },
                        { "FlashSuccess", playerStats.Flash_Successes },
                        { "HealthPointsRemovedTotal", playerStats.HealthPointsRemovedTotal },
                        { "HealthPointsDealtTotal", playerStats.HealthPointsDealtTotal },
                        { "ShotsFiredTotal", playerStats.ShotsFiredTotal },
                        { "ShotsOnTargetTotal", playerStats.ShotsOnTargetTotal },
                        { "EquipmentValue", playerStats.EquipmentValue },
                        { "MoneySaved", playerStats.MoneySaved },
                        { "KillReward", playerStats.KillReward },
                        { "LiveTime", playerStats.LiveTime },
                        { "HeadShotKills", playerStats.HeadShotKills },
                        { "CashEarned", playerStats.CashEarned },
                        { "EnemiesFlashed", playerStats.EnemiesFlashed }
                    };

                    string teamName = "Spectator";
                    if (player.TeamNum == 3){
                        teamName = reverseTeamSides["CT"].teamName;
                    } else if (player.TeamNum == 2 ) {
                        teamName = reverseTeamSides["TERRORIST"].teamName;
                    }

                    stats["TeamName"] = teamName;

                    playerStatsDictionary.Add(steamid64, stats);

                    // Populate PlayerStats instance
                    // Todo: Implement stats which are marked as 0 for now
                    PlayerStats playerStatsInstance = new()
                    {
                        Kills = playerStats.Kills,
                        Deaths = playerStats.Deaths,
                        Assists = playerStats.Assists,
                        FlashAssists = 0,
                        TeamKills = 0,
                        Suicides = 0,
                        Damage = playerStats.Damage,
                        UtilityDamage = playerStats.UtilityDamage,
                        EnemiesFlashed = playerStats.EnemiesFlashed,
                        FriendliesFlashed = 0,
                        KnifeKills = 0,
                        HeadshotKills = playerStats.HeadShotKills,
                        RoundsPlayed = roundsPlayed,
                        BombDefuses = 0,
                        BombPlants = 0,
                        Kills1 = 0,
                        Kills2 = playerStats.Enemy2Ks,
                        Kills3 = playerStats.Enemy3Ks,
                        Kills4 = playerStats.Enemy4Ks,
                        Kills5 = playerStats.Enemy5Ks,
                        OneV1s = playerStats.I1v1Wins,
                        OneV2s = playerStats.I1v2Wins,
                        OneV3s = 0,
                        OneV4s = 0,
                        OneV5s = 0,
                        FirstKillsT = 0,
                        FirstKillsCT = 0,
                        FirstDeathsT = 0,
                        FirstDeathsCT = 0,
                        TradeKills = 0,
                        Kast = 0,
                        Score = player.Score,
                        Mvps = player.MVPs,
                    };

                    StatsPlayer statsPlayer = new()
                    {
                        SteamId = steamid64.ToString(),
                        Name = player.PlayerName,
                        Stats = playerStatsInstance
                    };

                    int ctTeamNum = reverseTeamSides["CT"] == matchzyTeam1 ? 1 : 2;
                    int tTeamNum = reverseTeamSides["TERRORIST"] == matchzyTeam1 ? 1 : 2;

                    if (player.TeamNum == 3){
                        if (ctTeamNum == 1) playerStatsListTeam1.Add(statsPlayer);
                        if (ctTeamNum == 2) playerStatsListTeam2.Add(statsPlayer);
                    } else if (player.TeamNum == 2 ) {
                        if (tTeamNum == 1) playerStatsListTeam1.Add(statsPlayer);
                        if (tTeamNum == 2) playerStatsListTeam2.Add(statsPlayer);
                    }
                }
            }
            catch (Exception e)
            {
                Log($"[GetPlayerStatsDict FATAL] An error occurred: {e.Message}");
            }

            return (playerStatsDictionary, playerStatsListTeam1, playerStatsListTeam2);
        }

        static string RemoveSpecialCharacters(string input)
        {
            Regex regex = new("[^a-zA-Z0-9 _-]");
            return regex.Replace(input, "");
        }

        private void Log(string message) {
            Console.WriteLine("[MatchZy] " + message);
        }

        private void AutoStart()
        {
            Log($"[AutoStart] autoStartMode: {autoStartMode}");
            if (autoStartMode == 0)
            {
                StartSleepMode();
            }
            if (autoStartMode == 1)
            {
                readyAvailable = true;
                StartWarmup();
            }
            if (autoStartMode == 2)
            {
                StartPracticeMode();
            }
        }

        public int GetGameMode() {
            var convar = ConVar.Find("game_mode");
            if (convar != null) {
                return convar.GetPrimitiveValue<int>();
            }
            return -1;
        }

        public int GetGameType() {
            var convar = ConVar.Find("game_type");
            if (convar != null) {
                return convar.GetPrimitiveValue<int>();
            }
            return -1;
        }

        public void SetCorrectGameMode() {
            ConVar.Find("game_mode")!.SetValue(matchConfig.Wingman ? 2 : 1);
            ConVar.Find("game_type")!.SetValue(0); // Classic GameType
        }

        public bool IsMapReloadRequiredForGameMode(bool wingman)
        {
            int expectedMode = wingman ? 2 : 1;
            if (GetGameMode() != expectedMode || GetGameType() != 0) 
            {
                return true;
            }
            return false;
        }
    }
}
