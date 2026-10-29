namespace LogMark.Services.Agents;

/// <summary>
/// Startup repair pass for installed skill packs, the sibling of
/// WiringSelfHeal: reinstalls packs whose shipped content has moved and whose
/// installed copy the user has not edited. Best-effort - it never throws into
/// a product's startup, and a pack it cannot classify is left alone for the
/// user to resolve from Settings.
///
/// Exists because an installed pack was otherwise permanently frozen at
/// whatever revision was first installed: nothing compared it against the
/// shipped pack, and no surface reported the difference. A skill edited at the
/// source shipped dead while the tests pinning its new text stayed green.
/// </summary>
public static class SkillsSelfHeal
{
    public static int Run(SkillsService skills, Action<string>? log = null)
    {
        try
        {
            var healed = skills.SelfHeal();
            if (healed > 0) log?.Invoke($"Skill packs updated: {healed}");
            return healed;
        }
        catch (Exception ex)
        {
            log?.Invoke($"Skill-pack self-heal skipped: {ex.Message}");
            return 0;
        }
    }
}
