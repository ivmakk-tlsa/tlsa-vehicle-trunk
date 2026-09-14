using System;
using System.Collections.Generic;
using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using BepInEx.Unity.IL2CPP;
using DeadReckoned.Core.Events;
using Game;
using Game.Actors;
using Game.Data;
using Game.Data.Collections;
using Game.Data.Items;
using Game.Data.States;
using Game.Logic.Controllers;
using Game.Inputs;
using Game.Logic.Interaction;
using Game.Missions;
using Game.Props;
using Game.UI.Missions;
using Game.UI.Missions.Dialogs;
using HarmonyLib;
using Il2CppInterop.Runtime;
using UnityEngine;

namespace VehicleTrunk;

[BepInPlugin(PluginGuid, "VehicleTrunk", "1.0.0")]
public class Plugin : BasePlugin
{
    public const string PluginGuid = "com.ivmakk.tlsa.vehicletrunk";
    internal static new ManualLogSource Log;

    internal static ConfigEntry<float> TrunkWeightCapacity;
    internal static ConfigEntry<bool> Verbose;

    internal static void V(string msg)
    {
        if (Verbose.Value)
        {
            Log.LogDebug(msg);
        }
    }

    public override void Load()
    {
        Log = base.Log;

        TrunkWeightCapacity = Config.Bind("General", "TrunkWeightCapacity", 100f, "Weight capacity of the trunk");
        Verbose = Config.Bind("General", "Verbose", false, "Log trunk activity at Debug level");
        TrunkWeightCapacity.SettingChanged += (_, _) => Trunk.ApplyCapacity();

        new Harmony(PluginGuid).PatchAll(typeof(Plugin).Assembly);

        Log.LogInfo($"VehicleTrunk loaded (capacity {TrunkWeightCapacity.Value})");
    }
}

// The trunk is a plain ItemCollection the game does not know about. The game's own StashDialog
// is pointed at it for the duration of one OnOpened call (see DialogPatches), and its items are
// marked with a flag bit so the save's id registry can rebuild the collection on load.
internal static class Trunk
{
    private static ItemCollection s_items;
    private static ItemCollectionItemEvent s_added;
    private static ItemCollectionItemEvent s_removed;

    // Set between the car interaction and the dialog's OnOpened; tells the swap to happen.
    internal static bool OpeningTrunk;
    // Set while the stash dialog currently shows the trunk.
    internal static bool DialogOpen;

    internal static ItemCollection Items
    {
        get
        {
            if (s_items == null)
            {
                Create();
            }
            return s_items;
        }
    }

    private static void Create()
    {
        s_items = new ItemCollection();
        ApplyCapacity();
        s_added = DelegateSupport.ConvertDelegate<ItemCollectionItemEvent>(new Action<ItemCollection, Item>(OnAdded));
        s_removed = DelegateSupport.ConvertDelegate<ItemCollectionItemEvent>(new Action<ItemCollection, Item>(OnRemoved));
        s_items.Added = s_added;
        s_items.Removed = s_removed;
    }

    internal static void ApplyCapacity()
    {
        if (s_items == null)
        {
            return;
        }
        s_items.WeightCapacity = TrunkRules.ClampCapacity(Plugin.TrunkWeightCapacity.Value);
    }

    private static void OnAdded(ItemCollection collection, Item item)
    {
        if (item == null)
        {
            return;
        }
        item.m_Flags = TrunkRules.EnterFlags(item.m_Flags);
        Plugin.V($"trunk add: {Describe(item)} weight {collection.WeightTotal:0.##}/{collection.WeightCapacity:0.##}");
    }

    private static bool s_rebuilding;

    private static void OnRemoved(ItemCollection collection, Item item)
    {
        if (item == null || s_rebuilding)
        {
            return;
        }
        item.m_Flags = TrunkRules.LeaveFlags(item.m_Flags);
        Plugin.V($"trunk remove: {Describe(item)} weight {collection.WeightTotal:0.##}/{collection.WeightCapacity:0.##}");
    }

    internal static string Describe(Item item)
    {
        try
        {
            string model = item.Model != null ? item.Model.name : "<null>";
            return $"#{item.Id} {model} x{item.Quantity}";
        }
        catch (Exception)
        {
            return "<item>";
        }
    }

    // Every Item registered in the current save's id registry. The registry is an IL2CPP
    // Dictionary, so it is walked with the enumerator by hand.
    internal static List<Item> RegisteredItems()
    {
        var result = new List<Item>();
        var state = IdLookup.CurrentState;
        var lookup = state?.Lookup;
        if (lookup == null)
        {
            return result;
        }
        var e = lookup.Values.GetEnumerator();
        while (e.MoveNext())
        {
            var item = e.Current?.TryCast<Item>();
            if (item != null)
            {
                result.Add(item);
            }
        }
        return result;
    }

    // Rebuild the trunk from the flagged items in the registry. Runs once per new game (empty
    // registry, so the trunk resets) and once per load, before the game state is read.
    internal static void Rebuild()
    {
        var items = Items;
        s_rebuilding = true;
        try
        {
            while (items.Count > 0)
            {
                items.Remove(items[0]);
            }
        }
        finally
        {
            s_rebuilding = false;
        }
        int count = 0;
        foreach (var item in RegisteredItems())
        {
            if (!TrunkRules.ShouldRebuild(item.m_Flags, item.IsDisposed))
            {
                continue;
            }
            items.Add(item, ItemCollection.AddOptions.None);
            count++;
        }
        ApplyCapacity();
        Plugin.V($"VehicleTrunk: rebuild, {count} item(s), weight {items.WeightTotal:0.##}/{items.WeightCapacity:0.##}");
    }

    // Wipe on death: scan the registry rather than the collection so a flagged item the
    // collection missed cannot come back on the next load.
    internal static void Wipe()
    {
        // Empty the collection one item at a time first, so WeightTotal drops to 0 (Clear leaves it
        // stale, which showed the trunk holding weight with no items). s_rebuilding mutes OnRemoved
        // so the trunk flag survives for the registry scan below.
        if (s_items != null)
        {
            s_rebuilding = true;
            try
            {
                while (s_items.Count > 0)
                {
                    s_items.Remove(s_items[0]);
                }
            }
            finally
            {
                s_rebuilding = false;
            }
        }

        int count = 0;
        foreach (var item in RegisteredItems())
        {
            if ((item.m_Flags & TrunkRules.TrunkFlag) == 0)
            {
                continue;
            }
            item.m_Flags = TrunkRules.LeaveFlags(item.m_Flags);
            Plugin.V($"trunk wipe: {Describe(item)}");
            try
            {
                item.Dispose();
            }
            catch (Exception e)
            {
                Plugin.Log.LogWarning($"VehicleTrunk: dispose of {Describe(item)} failed: {e.Message}");
            }
            IdLookup.Deallocate(item.Cast<IIdentifiable>());
            count++;
        }
        Plugin.V($"VehicleTrunk: wiped {count} item(s) on death, weight {(s_items != null ? s_items.WeightTotal : 0f):0.##}");
    }

    internal static PlayerActor Player()
    {
        try
        {
            return MissionController.Active?.PlayerActor;
        }
        catch (Exception)
        {
            return null;
        }
    }

    // Carrying something that clashes with the trunk. A supply bag on the back is handed in at the
    // same interactable the trunk reuses, so it blocks the trunk (the vanilla hand-in must run). A
    // fuel can (FuelDrop, also carried on the back) is deposited at the car's separate fuel
    // interactable, so it does not block the trunk: the trunk stays reachable with a fuel can on the
    // back. An item in hand (a throwable) still blocks.
    internal static bool PlayerIsCarrying(PlayerActor player)
    {
        if (player == null)
        {
            return false;
        }
        if (PlayerCarryingBag(player))
        {
            return true;
        }
        var hand = player.CarryInHandController;
        return hand != null && hand.m_CurrentItem != null;
    }

    // A supply bag on the back (any on-back item that is not a fuel can). The game switches the car's
    // supply object on for the bag hand-in, so the mod must not switch that object off while a bag is
    // carried, or the hand-in has no interactable.
    internal static bool PlayerCarryingBag(PlayerActor player)
    {
        var back = player?.CarryOnBackController;
        var backItem = back != null ? back.CurrentItem : null;
        return backItem != null && backItem.TryCast<FuelDrop>() == null;
    }
}

// The car's supply hand-in is the trunk's interaction. With a bag on the back the shipped
// hand-in runs untouched; with empty hands the interaction opens the trunk instead.
[HarmonyPatch(typeof(PlayerVehicleSupplyInteraction))]
internal static class VehicleInteractionPatches
{
    [HarmonyPostfix]
    [HarmonyPatch(nameof(PlayerVehicleSupplyInteraction.CanInteractionBeStarted))]
    private static void CanStart(Interactable interactable, Actor actor, ref bool __result)
    {
        if (__result)
        {
            return;
        }
        try
        {
            var player = actor?.TryCast<PlayerActor>();
            if (TrunkRules.ShowTrunk(player != null, Trunk.PlayerIsCarrying(player)))
            {
                __result = true;
            }
        }
        catch (Exception e)
        {
            Plugin.Log.LogWarning($"VehicleTrunk: CanInteractionBeStarted postfix failed: {e.Message}");
        }
    }

    [HarmonyPrefix]
    [HarmonyPatch(nameof(PlayerVehicleSupplyInteraction.OnInteractionCompleted))]
    private static bool Completed(Interactable interactable)
    {
        if (VehicleActivation.Replaying)
        {
            return true;
        }
        try
        {
            var player = Trunk.Player();
            if (!TrunkRules.ShowTrunk(player != null, Trunk.PlayerIsCarrying(player)))
            {
                return true;
            }
            // The trunk opens as sector stash 1, so the game's OnOpened will index
            // m_SectorStashes[0]. On a map with no sector-stash slot there is nothing to swap,
            // so leave the vanilla hand-in rather than raise a request the game cannot serve.
            var state = GameManager.ActiveGameState;
            if (state == null || state.m_SectorStashes == null || state.m_SectorStashes.Length < 1)
            {
                Plugin.Log.LogWarning("VehicleTrunk: no sector stash slot for the trunk; leaving the vanilla hand-in");
                return true;
            }
            var evt = EventCache.Get<StashContainerOpenRequestEvent>();
            if (evt == null)
            {
                Plugin.Log.LogWarning("VehicleTrunk: no StashContainerOpenRequestEvent in the event cache");
                return true;
            }
            evt.Container = null;
            evt.SectorIndex = 1;
            evt.CommitItemsOnClose = false;
            evt.OnClosedCallback = null;
            Trunk.OpeningTrunk = true;
            Plugin.V("trunk: open requested at the car");
            EventManager.Raise(evt.Cast<IEvent>());
            return false;
        }
        catch (Exception e)
        {
            Trunk.OpeningTrunk = false;
            Plugin.Log.LogWarning($"VehicleTrunk: open request failed: {e.Message}");
            return true;
        }
    }
}

// A real stash prop must never see a stale open-trunk flag.
[HarmonyPatch(typeof(StashContainer), nameof(StashContainer.OnInteractionCompleted))]
internal static class StashContainerPatches
{
    [HarmonyPrefix]
    private static void ClearFlag()
    {
        Trunk.OpeningTrunk = false;
    }
}

// The dialog opens as sector stash 1: in main-stash mode it greys every item without the game's
// stashed bit (the Armory rule), in sector mode the only gate is weight. StashDialog.OnOpened reads
// GameState.m_SectorStashes[0] inline and binds its lists in the same call, so that slot holds the
// trunk only across that one synchronous call.
[HarmonyPatch(typeof(StashDialog))]
internal static class DialogPatches
{
    private static ItemCollection s_savedSlot;
    private static GameState s_swappedState;

    [HarmonyPrefix]
    [HarmonyPatch(nameof(StashDialog.OnOpened))]
    private static void OpenedPrefix(StashDialog __instance)
    {
        if (!Trunk.OpeningTrunk)
        {
            return;
        }
        try
        {
            var state = GameManager.ActiveGameState;
            if (state == null)
            {
                Plugin.Log.LogWarning("VehicleTrunk: no active game state at dialog open");
                Trunk.OpeningTrunk = false;
                return;
            }
            var slots = state.m_SectorStashes;
            if (slots == null || slots.Length < 1)
            {
                Plugin.Log.LogWarning("VehicleTrunk: no sector stash slots at dialog open");
                Trunk.OpeningTrunk = false;
                return;
            }
            s_savedSlot = slots[0];
            s_swappedState = state;
            slots[0] = Trunk.Items;
            Trunk.DialogOpen = true;
            Plugin.V($"trunk: dialog open, sector {__instance.SectorIndex}, commit {__instance.CommitItemsOnClose}, {Trunk.Items.Count} item(s)");
        }
        catch (Exception e)
        {
            Plugin.Log.LogWarning($"VehicleTrunk: swap failed: {e.Message}");
            Restore();
        }
    }

    // A finalizer runs even when OnOpened throws, so the real main stash always comes back.
    [HarmonyFinalizer]
    [HarmonyPatch(nameof(StashDialog.OnOpened))]
    private static void OpenedFinalizer(StashDialog __instance, Exception __exception)
    {
        Restore();
        Trunk.OpeningTrunk = false;
        if (__exception != null)
        {
            Plugin.Log.LogWarning($"VehicleTrunk: StashDialog.OnOpened threw: {__exception.Message}");
            Trunk.DialogOpen = false;
            return;
        }
    }

    // The dialog adds a moved item without the auto-stack option (the game merges stacks only on
    // the next map load), so a round moved back sits on its own row. Merge with the game's own
    // consolidate after each move while the trunk is showing.
    [HarmonyPostfix]
    [HarmonyPatch(nameof(StashDialog.MoveFromStashToInventory))]
    private static void MovedToInventory(StashDialog __instance)
    {
        if (Trunk.DialogOpen)
        {
            Consolidate(__instance.m_Inventory, "inventory");
        }
    }

    [HarmonyPostfix]
    [HarmonyPatch(nameof(StashDialog.MoveFromInventoryToStash))]
    private static void MovedToStash()
    {
        if (Trunk.DialogOpen)
        {
            Consolidate(Trunk.Items, "trunk");
        }
    }

    private static void Consolidate(ItemCollection items, string label)
    {
        if (items == null)
        {
            return;
        }
        try
        {
            int before = items.Count;
            items.ConsolidateStackableItems();
            if (items.Count != before)
            {
                Plugin.V($"trunk: {label} stacks merged {before} -> {items.Count}");
            }
        }
        catch (Exception e)
        {
            Plugin.Log.LogWarning($"VehicleTrunk: consolidate {label} failed: {e.Message}");
        }
    }

    // Runs from OnFilterChanged, after the game wrote its own stash name; keep the filter suffix.
    [HarmonyPostfix]
    [HarmonyPatch(nameof(StashDialog.UpdateStashTitle))]
    private static void TitlePostfix(StashDialog __instance)
    {
        if (!Trunk.DialogOpen)
        {
            return;
        }
        try
        {
            var text = __instance.m_StashTitle?.TextField;
            if (text == null)
            {
                return;
            }
            string current = text.Text ?? "";
            int slash = current.IndexOf('/');
            text.SetText(slash >= 0 ? "Trunk " + current.Substring(slash) : "Trunk");
        }
        catch (Exception e)
        {
            Plugin.V($"trunk: title not set: {e.Message}");
        }
    }

    [HarmonyPostfix]
    [HarmonyPatch(nameof(StashDialog.OnClosed))]
    private static void ClosedPostfix()
    {
        if (Trunk.DialogOpen)
        {
            Plugin.V($"trunk: dialog closed, {Trunk.Items.Count} item(s), weight {Trunk.Items.WeightTotal:0.##}");
            VehicleActivation.ReplayClose();
        }
        Trunk.DialogOpen = false;
    }

    private static void Restore()
    {
        if (s_swappedState == null)
        {
            return;
        }
        try
        {
            var slots = s_swappedState.m_SectorStashes;
            if (slots != null && slots.Length >= 1)
            {
                slots[0] = s_savedSlot;
            }
        }
        finally
        {
            s_swappedState = null;
            s_savedSlot = null;
        }
    }
}

// The prompt over the car shows the game's stash icon when the trunk would open.
[HarmonyPatch(typeof(InteractionFloater), nameof(InteractionFloater.GetActionSprite))]
internal static class FloaterPatches
{
    [HarmonyPostfix]
    private static void Postfix(InteractionFloater __instance, ref Sprite __result)
    {
        try
        {
            var target = __instance.m_Target;
            if (target == null || target.GetComponent<PlayerVehicleSupplyInteraction>() == null)
            {
                return;
            }
            var player = Trunk.Player();
            if (!TrunkRules.ShowTrunk(player != null, Trunk.PlayerIsCarrying(player)))
            {
                return;
            }
            var stash = __instance.ActionIcons?.Stash;
            if (stash != null)
            {
                __result = stash;
            }
        }
        catch (Exception e)
        {
            Plugin.V($"trunk: floater icon failed: {e.Message}");
        }
    }
}

// The id registry is initialized once per new game and once per load; rebuild the trunk there.
[HarmonyPatch(typeof(IdLookup), nameof(IdLookup.Initialize))]
internal static class RegistryPatches
{
    [HarmonyPostfix]
    private static void Postfix()
    {
        try
        {
            Trunk.Rebuild();
        }
        catch (Exception e)
        {
            Plugin.Log.LogWarning($"VehicleTrunk: rebuild failed: {e.Message}");
        }
    }
}

[HarmonyPatch(typeof(GameState), nameof(GameState.KillActiveSurvivor))]
internal static class DeathPatches
{
    [HarmonyPrefix]
    private static void Prefix()
    {
        try
        {
            Trunk.Wipe();
        }
        catch (Exception e)
        {
            Plugin.Log.LogWarning($"VehicleTrunk: wipe failed: {e.Message}");
        }
    }
}

// The car's SupplyInteractable object is inactive until a bag is carried (the game switches it on
// for the hand-in). The trunk needs it while the player carries nothing, so the object is kept
// active then and handed back to the game as soon as something is picked up.
[HarmonyPatch]
internal static class VehicleActivation
{
    private static PlayerMissionVehicle s_vehicle;
    private static bool s_activatedByMod;

    // ControllerUpdate is a per-frame reconciler on InteractionController.Update. It only needs to
    // converge, so it runs every CheckInterval frames instead of every frame. At 60 fps that is
    // about a quarter second worst-case before the prompt appears at the car or the object is handed
    // back, which is not noticeable. A frame counter, so the delay scales with the frame rate.
    private const int CheckInterval = 15;
    private static int s_frame;

    // True while the game's own hand-in completion runs for the trunk close (see ReplayClose).
    internal static bool Replaying;

    // The trunk lid opens in OnInteractionStarted and closes at the top of OnInteractionCompleted
    // (lid animation plus close sound), which the trunk skips to reach the dialog. With empty hands
    // that method returns right after the close part, so replaying it closes the lid.
    internal static void ReplayClose()
    {
        var supply = s_vehicle != null ? s_vehicle.m_SupplyInteractable : null;
        if (supply == null)
        {
            return;
        }
        Replaying = true;
        try
        {
            supply.OnInteractionCompleted(supply.GetComponent<Interactable>());
            Plugin.V("trunk: lid closed");
        }
        catch (Exception e)
        {
            Plugin.Log.LogWarning($"VehicleTrunk: lid close failed: {e.Message}");
        }
        finally
        {
            Replaying = false;
        }
    }

    [HarmonyPostfix]
    [HarmonyPatch(typeof(PlayerMissionVehicle), nameof(PlayerMissionVehicle.Start))]
    private static void VehicleStart(PlayerMissionVehicle __instance)
    {
        s_vehicle = __instance;
        s_activatedByMod = false;
        var supply = __instance.m_SupplyInteractable;
        Plugin.V($"vehicle: {__instance.gameObject.name}, supply object {(supply != null ? supply.gameObject.name + " active=" + supply.gameObject.activeSelf : "null")}");
    }

    [HarmonyPostfix]
    [HarmonyPatch(typeof(InteractionController), nameof(InteractionController.Update))]
    private static void ControllerUpdate()
    {
        if (s_vehicle == null)
        {
            return;
        }
        if (++s_frame < CheckInterval)
        {
            return;
        }
        s_frame = 0;
        try
        {
            var supply = s_vehicle.m_SupplyInteractable;
            if (supply == null)
            {
                return;
            }
            var go = supply.gameObject;
            var player = Trunk.Player();
            bool wantTrunk = TrunkRules.ShowTrunk(player != null, Trunk.PlayerIsCarrying(player));
            if (wantTrunk && !go.activeSelf)
            {
                go.SetActive(true);
                s_activatedByMod = true;
                Plugin.V("trunk: supply interactable switched on");
            }
            else if (!wantTrunk && !Trunk.PlayerCarryingBag(player) && s_activatedByMod && go.activeSelf)
            {
                go.SetActive(false);
                s_activatedByMod = false;
                Plugin.V("trunk: supply interactable handed back to the game");
            }
        }
        catch (Exception e)
        {
            Plugin.V($"trunk: activation failed: {e.Message}");
        }
    }
}
