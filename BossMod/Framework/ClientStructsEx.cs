using FFXIVClientStructs.FFXIV.Client.Game.Group;

namespace BossMod;

internal static class ClientStructsEx
{
    public static bool IsValidAllianceMember(this PartyMember member) => (member.Flags & 1) != 0;
}
