using System;
using System.Linq;
using CultistAccessibility.Core;
using SecretHistories.Commands;
using SecretHistories.UI;
using UnityEngine.InputSystem;

namespace CultistAccessibility.Tabletop
{
    /// <summary>
    /// Test-only shortcuts for developing the mod (config DeveloperTestKeys, off by default).
    /// Ctrl+Shift+F9 opens a Mansus portal from the first verb (the same command a recipe's portalEffect runs,
    /// see RecipeCompletionEffectCommand). Ctrl+Shift+F10 puts Health, Passion, Reason and Funds on the table.
    /// Ctrl+Shift+F11 ends the game with an ending (to test the ending and legacy screens). WARNING: reaching an
    /// ending unlocks a real Steam achievement and writes the game's achievements.json to Steam Cloud.
    /// Ctrl+Shift+F7 saves now (GameGateway.TryDefaultSave). Ctrl+Shift+F6 spawns the Despair countdown (despairdeath:
    /// signals an ending, greedy slot for Contentment); Ctrl+Shift+F5 adds a Contentment card.
    /// Any screen (HandleGlobalInput): Ctrl+Shift+F4 raises a game notification; Ctrl+Shift+F3 shows the menu's
    /// save/safe-mode messages (menu, display only: do not press their buttons) or the save error window (table);
    /// Ctrl+Shift+F8 in the menu loads the error scene (StageHand.LoadInfoScene), which can only be left by quitting.
    /// </summary>
    internal static class DeveloperTools
    {
        public static bool HandleGlobalInput(GameScreen screen)
        {
            if (ModConfig.DeveloperTestKeys == null || !ModConfig.DeveloperTestKeys.Value) return false;
            if (!(KeyInput.Ctrl && KeyInput.Shift)) return false;
            if (KeyInput.Pressed(Key.F4))
            {
                ShowTestNotification();
                return true;
            }
            if (KeyInput.Pressed(Key.F3))
            {
                if (screen == GameScreen.Menu) ShowMenuSaveMessages();
                else if (screen == GameScreen.Tabletop) ShowSaveError();
                return true;
            }
            if (KeyInput.Pressed(Key.F8) && screen == GameScreen.Menu)
            {
                try
                {
                    Watchman.Get<SecretHistories.Services.StageHand>().LoadInfoScene();
                }
                catch (Exception ex)
                {
                    Plugin.LogError("LoadInfoScene failed: " + ex);
                }
                return true;
            }
            return false;
        }

        private static int _menuMessageStep;

        /// <summary>
        /// Cycles the HintsHolder messages through the combinations UpdateAndShowMenu and RestoreFromBackup produce:
        /// corrupted save; peculiar save with safe mode; restoring failed; none.
        /// </summary>
        private static void ShowMenuSaveMessages()
        {
            try
            {
                var menu = UnityEngine.Object.FindObjectOfType<SecretHistories.Infrastructure.MenuScreenController>();
                if (menu == null) return;
                int step = _menuMessageStep++ % 4;
                menu.brokenSaveMessage.SetActive(step == 0);
                menu.suspiciousSaveMessage.SetActive(step == 1);
                menu.safeModeMessage.SetActive(step == 1);
                menu.noBackupSaveMessage.SetActive(step == 2);
                Speech.Say("Developer: save messages, step " + (step + 1) + " of 4.");
            }
            catch (Exception ex)
            {
                Plugin.LogError("ShowMenuSaveMessages failed: " + ex);
            }
        }

        /// <summary>The same call GameGateway.TryDefaultSave makes when saving throws.</summary>
        private static void ShowSaveError()
        {
            try
            {
                Watchman.Get<Notifier>().ShowCustomWindow(new CustomNotificationWindowArgs
                {
                    WindowId = CustomNotificationWindowId.ShowSaveError,
                    AdditionalText = "\n'<b>Developer test</b>'"
                });
            }
            catch (Exception ex)
            {
                Plugin.LogError("ShowSaveError failed: " + ex);
            }
        }

        public static bool HandleInput()
        {
            if (ModConfig.DeveloperTestKeys == null || !ModConfig.DeveloperTestKeys.Value) return false;
            if (!(KeyInput.Ctrl && KeyInput.Shift)) return false;
            if (KeyInput.Pressed(Key.F9))
            {
                OpenPortal("wood");
                return true;
            }
            if (KeyInput.Pressed(Key.F10))
            {
                AddCards("health", "passion", "reason", "funds");
                return true;
            }
            if (KeyInput.Pressed(Key.F11))
            {
                EndGame();
                return true;
            }
            if (KeyInput.Pressed(Key.F7))
            {
                SaveNow();
                return true;
            }
            if (KeyInput.Pressed(Key.F6))
            {
                SpawnSituation("despair", "despairdeath");
                return true;
            }
            if (KeyInput.Pressed(Key.F5))
            {
                AddCards("contentment");
                return true;
            }
            return false;
        }

        /// <summary>Spawns a verb running a recipe, the way Situation.AdditionalRecipeSpawnToken does.</summary>
        private static void SpawnSituation(string verbId, string recipeId)
        {
            try
            {
                var s = GameAccess.TableSituations().FirstOrDefault();
                if (s == null) return;
                var creation = new SituationCreationCommand(verbId).WithRecipeAboutToActivate(recipeId);
                new SpawnNewTokenFromThisOneCommand(creation, s.Token.Location.AtSpherePath, new Context(Context.ActionSource.JustSpawned)).ExecuteOn(s.Token);
                Speech.Say("Developer: spawned " + verbId + ".");
            }
            catch (Exception ex)
            {
                Plugin.LogError("SpawnSituation failed: " + ex);
            }
        }

        private static async void SaveNow()
        {
            try
            {
                bool ok = await Watchman.Get<SecretHistories.Infrastructure.GameGateway>().TryDefaultSave();
                Speech.Say(ok ? "Developer: saved." : "Developer: save failed.");
            }
            catch (Exception ex)
            {
                Plugin.LogError("SaveNow failed: " + ex);
            }
        }

        /// <summary>Any screen, Ctrl+Shift+F4: a notification through the game's own Concursum.ShowNotification.</summary>
        public static void ShowTestNotification()
        {
            try
            {
                Watchman.Get<SecretHistories.Services.Concursum>().ShowNotification(
                    new SecretHistories.Services.NotificationArgs("Developer test notification", "This text came from the game's notification window."));
            }
            catch (Exception ex)
            {
                Plugin.LogError("ShowTestNotification failed: " + ex);
            }
        }

        private static void OpenPortal(string portalId)
        {
            try
            {
                var s = GameAccess.TableSituations().FirstOrDefault();
                if (s == null) { Speech.Say("No verb to open a portal from."); return; }
                var cmd = new SpawnNewTokenFromThisOneCommand(new IngressCreationCommand(portalId), s.Token.Location.AtSpherePath, Context.Unknown());
                cmd.ExecuteOn(s.Token);
                Speech.Say("Developer: portal " + portalId + " spawned.");
            }
            catch (Exception ex)
            {
                Plugin.LogError("OpenPortal failed: " + ex);
            }
        }

        /// <summary>Ends the current life with the game's default ending (GameGateway.EndGame, as an ending recipe does).</summary>
        private static void EndGame()
        {
            try
            {
                var s = GameAccess.TableSituations().FirstOrDefault();
                if (s == null) return;
                Watchman.Get<SecretHistories.Infrastructure.GameGateway>().EndGame(GameAccess.Compendium.GetEntityById<SecretHistories.Entities.Ending>("deathofthebody"), s.Token);
                Speech.Say("Developer: ending the game.");
            }
            catch (Exception ex)
            {
                Plugin.LogError("EndGame failed: " + ex);
            }
        }

        private static void AddCards(params string[] ids)
        {
            try
            {
                var table = GameAccess.TabletopSphere;
                if (table == null) return;
                foreach (var id in ids) table.ProvisionElementToken(id, 1);
                Speech.Say("Developer: cards added.");
            }
            catch (Exception ex)
            {
                Plugin.LogError("AddCards failed: " + ex);
            }
        }
    }
}
