namespace BossMod;

// Classifies an action as physical or magical damage so that raidwide/tankbuster hints can say which kind of
// mitigation is the relevant one.
//
// Source is the Action sheet's AttackType link - the same field the game itself keys 'Physical Vulnerability Up'
// (物理受傷加重) vs 'Magic Vulnerability Up' (魔法受傷加重) off. Rows of the AttackType sheet, verified against the
// TC 7.20 dump:
//   1 斬 / 2 突 / 3 打 / 4 射 -> physical
//   5 魔法                    -> magical
//   0 (no attack type), 6 (breath), 7 (sound), 8 (limit break), and unresolved links -> NOT classifiable.
//
// Anything in that last group is reported as Unknown and rendered as '?'. Guessing here would be worse than saying
// nothing: a wrong label sends the player into the wrong cooldown.
public static class ActionDamageType
{
    public enum Kind
    {
        None = 0, // component has no action to look at - draw nothing at all
        Physical = 1,
        Magical = 2,
        Unknown = 3 // there is an action, but its attack type does not map onto physical/magical
    }

    private const uint _attackTypeSlashing = 1;
    private const uint _attackTypeShot = 4;
    private const uint _attackTypeMagic = 5;

    // action ids are stable for the whole session, so the sheet lookup is done once per action
    private static readonly Dictionary<uint, Kind> _cache = [];

    public static Kind Classify(uint actionId)
    {
        if (actionId == default)
            return Kind.None;
        if (_cache.TryGetValue(actionId, out var cached))
            return cached;

        var kind = Kind.Unknown;
        var row = Service.LuminaRow<Lumina.Excel.Sheets.Action>(actionId);
        if (row != null)
        {
            var attackType = row.Value.AttackType.RowId;
            kind = attackType >= _attackTypeSlashing && attackType <= _attackTypeShot ? Kind.Physical
                : attackType == _attackTypeMagic ? Kind.Magical
                : Kind.Unknown;
        }
        return _cache[actionId] = kind;
    }

    // consensus over several actions; if they disagree we cannot label the hint, so report Unknown rather than pick one
    public static Kind Classify(uint[] actionIds)
    {
        var len = actionIds.Length;
        if (len == 0)
            return Kind.None;

        var res = Kind.None;
        for (var i = 0; i < len; ++i)
        {
            var k = Classify(actionIds[i]);
            if (k == Kind.None)
                continue;
            if (res == Kind.None)
                res = k;
            else if (res != k)
                return Kind.Unknown;
        }
        return res;
    }

    public static string Label(Kind kind) => kind switch
    {
        Kind.Physical => Loc.T("DMG_Physical", "[phys]"),
        Kind.Magical => Loc.T("DMG_Magical", "[magic]"),
        Kind.Unknown => Loc.T("DMG_Unknown", "[?]"),
        _ => ""
    };
}
