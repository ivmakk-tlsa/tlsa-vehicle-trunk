using System;

namespace VehicleTrunk;

// Game-free trunk rules: no BepInEx or Il2Cpp references, so this file is unit-tested directly.
public static class TrunkRules
{
    public const int TrunkFlag = 1 << 16;

    // The game's own "went through a stash" bit. The stash dialog sets it on every item it moves
    // in either direction, and the hub Armory accepts only items that carry it.
    public const int StashedFlag = 2;

    // Remembers whether the item carried StashedFlag before it entered the trunk, so leaving the
    // trunk restores the game's bit instead of granting Armory access to everything.
    public const int WasStashedFlag = 1 << 17;

    public static int EnterFlags(int flags)
    {
        if ((flags & TrunkFlag) != 0)
        {
            return flags;
        }
        flags |= TrunkFlag;
        if ((flags & StashedFlag) != 0)
        {
            flags |= WasStashedFlag;
        }
        else
        {
            flags &= ~WasStashedFlag;
        }
        return flags;
    }

    public static int LeaveFlags(int flags)
    {
        if ((flags & WasStashedFlag) == 0)
        {
            flags &= ~StashedFlag;
        }
        return flags & ~(TrunkFlag | WasStashedFlag);
    }

    public static bool ShouldRebuild(int flags, bool isDisposed)
    {
        return (flags & TrunkFlag) != 0 && !isDisposed;
    }

    public static float ClampCapacity(float value)
    {
        if (float.IsNaN(value) || value < 0f)
        {
            return 0f;
        }

        return value;
    }

    public static bool ShowTrunk(bool isPlayer, bool isCarrying)
    {
        return isPlayer && !isCarrying;
    }
}
