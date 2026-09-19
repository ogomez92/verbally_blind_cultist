# Verified patch targets

All patches are applied by name in `Patches/PatchRegistry.cs`; every success or failure is logged
("Patched X.Y" / "Patch target not found"). Signatures verified in the decompiled `SecretHistories.Main`
(game 2026.1.g.2). After a game update, grep `BepInEx\LogOutput.log` for "not found" and "Failed to patch".

| Type | Method | Signature | Patch | Purpose |
|---|---|---|---|---|
| `SecretHistories.Infrastructure.UIController` | `Input_Truck_Key`, `Input_Pedestal_Key` | `(InputAction.CallbackContext)` | prefix, returns false while `InputGate.BlockCameraKeys` | Arrow keys pan the camera in the game; the mod uses them for navigation. |
| `SecretHistories.Infrastructure.UIController` | `Input_Zoom_Key`, `Input_ZoomClose`, `Input_ZoomMid`, `Input_ZoomFar`, `Input_Pause`, `Input_NormalSpeed`, `Input_FastSpeed`, `Input_Slower`, `Input_Faster`, `Input_GroupAllStacks`, `Input_StartRecipe`, `Input_NextComplete`, `Input_CollectAll`, `Input_Abort` | `(InputAction.CallbackContext)` | prefix, returns false while a mod modal is open | Keys pressed inside help or a picker must not also act on the game (Escape closing a picker must not open Options). |
| `SecretHistories.Infrastructure.MenuScreenController` | `OnAbort` | `()` | prefix, same rule | Escape in the menu while a mod modal is open. |
| `SecretHistories.UI.NotificationWindow` | `Show` | `()` | postfix, reads Title/Description/AdditionalText next frame | Every pop-up notification (mismatch messages, "can't merge", save errors, menu notices, achievement toasts). |
| `Heart` | `RespondToSpeedControlCommand` | `(SpeedControlEventArgs)` | prefix captures `GetEffectiveGameSpeed()`, postfix announces a change | Pause / normal / fast announcements from any source (keys, buttons, menus, Mansus). |
| `SecretHistories.Infrastructure.GameGateway` | `EndGame` | `(Ending, Token)` (async void, patches the kickoff) | prefix | "This life is ending" during the several seconds of ending animation. |
| `SecretHistories.UI.ElementStack` | `ChangeTo` | `(string)` | prefix captures the old label (table cards only), postfix announces "X becomes Y" | Decay and transformation of cards on the table. |
| `SecretHistories.UI.ElementStack` | `Retire` | `(RetirementVFX)` | prefix | Cards on the table that vanish with a visible effect (not merges: `None`; not uniqueness removals: `CardHide`). |
| `SecretHistories.Spheres.TabletopSphere` | `AcceptToken` | `(Token, Context)` | postfix, batches for 0.6 s | Cards arriving on the table from game effects (skips PlayerDrag, PlayerDumpAll, CalvedStack, Loading...). |
| `SecretHistories.Spheres.Angels.GreedyAngel` | `GrabStack` (private) | `(Token)`, injects `____thresholdSphereToGrabTo` | prefix | "Verb takes card" when a greedy slot pulls a card off the table. |
| `SecretHistories.UI.ElementStack` | `InteractWithIncoming` | `(Token)` | prefix records an arriving card (incoming token not on a `TabletopSphere`), postfix queues "Funds x 9, now 11" | Cards that merge into a stack on arrival (`TokenTravelItinerary.TryMergeWithTokenAtDestination`) never reach `TabletopSphere.AcceptToken`. |
| `SecretHistories.UI.TokenTravelItinerary` | `Arrive` | `(Token, Context)` | prefix records `context.actionSource` (the departure context is carried through the travel animation), postfix clears it | A merge on arrival caused by the player (a card taken out of a slot, a result taken: `PlayerDrag`) is not announced as an arrival. |
| `SecretHistories.Infrastructure.UIController` | `Input_StartRecipe`, `Input_CollectAll` | `(InputAction.CallbackContext)` | prefix captures the open verb's `StateEnum` (Inchoate = no window), postfix speaks "No verb window is open" / "Cannot start" / "Nothing to collect yet" / "Collecting" | The game's S and C keys otherwise do nothing silently. |

## Game APIs used without patches (read or called)
- State: `Watchman.Get<HornedAxe>().GetRegisteredSituations()`, `GetSpheresOfCategory(SphereCategory.World)`,
  `Situation.StateIdentifier/TimeRemaining/CurrentRecipe/MetafictionalLabel/MetafictionalDescription/GetDominion`,
  `Situation.GetAspects(false)`, `Heart.IsPaused/GetEffectiveGameSpeed`, `Numa.IsOtherworldActive` (+ private
  `_currentOtherworld`), `Otherworld.Dominions`, `OtherworldDominion.EgressSphere`, `Stable.Protag()`,
  `Legacy.StatusBarElements`, `StatusBarElementSpec.Ids`, `HornedAxe.GetAspectsInContext().AspectsExtant`.
- Actions: `Situation.OpenAt/Close/TryStart/Conclude`, `ThresholdSphere.TryAcceptToken`, `Token.CalveToken`,
  `Token.RequestHomeLocationFromCurrentSphere`, `Token.GoAway(Context)`, `ITokenPayload.Unshroud`,
  `Ingress.Conclude` (portal), `NotesSphere.ShowPrevPage/ShowNextPage`.
- Private fields read by reflection (logged if missing): `SliderSettingControl.SliderHint/SliderValueLabel`,
  `OptionsPanelTab.TabText`, `OptionsPanel.currentTab`, `AchievementCategoryTab.tabText`, `ProemSlot._label/_desc`,
  `NewGameScreenController.AvailableLegaciesForEnding/selectedLegacy`, `Numa._currentOtherworld`,
  `Otherworld._activeIngress`, `Notifier.SaveErrorWindow` (the table's save error dialog).
