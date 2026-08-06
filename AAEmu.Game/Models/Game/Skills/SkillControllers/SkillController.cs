using AAEmu.Game.Models.Game.Skills.Plots;
using AAEmu.Game.Models.Game.Skills.Templates;
using AAEmu.Game.Models.Game.Units;

using NLog;

namespace AAEmu.Game.Models.Game.Skills.SkillControllers;

public class SkillController
{
    private static Logger Logger { get; } = LogManager.GetCurrentClassLogger();

    public enum SCState
    {
        Created,
        Running,
        Ended
    }
    public SkillControllerTemplate Template { get; set; }
    public Unit Owner { get; protected set; }
    public BaseUnit Target { get; protected set; }

    public SCState State { get; protected set; }

    /// <summary>
    /// True when the owner's normal movement packets are the authoritative controller output.
    /// </summary>
    public virtual bool UsesClientMovement => false;

    protected SkillController()
    {

    }

    public virtual void Execute()
    {
        State = SCState.Running;
        Logger.Trace("SkillController: owner={0}:{1} entering execute state={2}",
            Owner?.Name ?? "<null>",
            Owner?.ObjId ?? 0,
            State);
    }

    public virtual void End()
    {
        State = SCState.Ended;
        if (Owner?.ActiveSkillController == this)
            Owner.ActiveSkillController = null;
        Logger.Trace("SkillController: owner={0}:{1} entering end state={2}",
            Owner?.Name ?? "<null>",
            Owner?.ObjId ?? 0,
            State);
    }

    /// <summary>
    /// Confirms that the owning client created the controller announced by the plot.
    /// The client-side controller type is a protocol value and must not be assumed to
    /// equal every server-side <see cref="SkillControllerKind"/> value.
    /// </summary>
    public virtual bool ConfirmClientController(byte scType, bool fallDamageImmune)
    {
        return false;
    }

    public static SkillController CreateSkillController(SkillControllerTemplate template, BaseUnit owner, BaseUnit target)
    {
        if (template == null || owner is not Unit)
            return null;

        SkillController controller;
        switch ((SkillControllerKind)template.KindId)
        {
            case SkillControllerKind.Floating:
                controller = new FloatingSkillController(template, owner, target);
                break;
            case SkillControllerKind.Leap:
                controller = new LeapSkillController(template, owner, target);
                break;
            case SkillControllerKind.Wandering:
                controller = new WanderingSkillController(template, owner, target);
                break;
            case SkillControllerKind.Dash:
                controller = new DashSkillController(template, owner, target);
                break;
            case SkillControllerKind.Rotate:
                controller = new RotateSkillController(template, owner, target);
                break;
            case SkillControllerKind.Rope:
                controller = new TimedSkillController(template, owner, target, ResolveClientDrivenDuration(template, 1000));
                break;
            case SkillControllerKind.Anchor:
                controller = new TimedSkillController(template, owner, target, ResolveClientDrivenDuration(template, 1500));
                break;
            case SkillControllerKind.Flowgraph:
                controller = new TimedSkillController(template, owner, target, ResolveClientDrivenDuration(template, 250));
                break;
            case SkillControllerKind.Impulse:
            case SkillControllerKind.Move:
            case SkillControllerKind.Crawl:
                controller = new TimedSkillController(template, owner, target, ResolveClientDrivenDuration(template, 500));
                break;
            default:
                PlotDiagnostics.UnsupportedController(template.Id, template.KindId);
                // Unknown target-data kinds still receive a finite lifecycle so the unit is
                // not left with a null/permanently-running controller.
                controller = new TimedSkillController(template, owner, target, 250);
                break;
        }

        controller.State = SCState.Created;
        Logger.Trace("SkillController: created {0} for template={1}, kind={2}",
            controller.GetType().Name,
            template.Id,
            template.KindId);
        return controller;
    }

    private static int ResolveClientDrivenDuration(SkillControllerTemplate template, int fallback)
    {
        // The parameter layout differs by kind. Select a plausible millisecond field but
        // reject rope lengths/angles measured in the hundreds of thousands.
        foreach (var value in template.Value)
        {
            if (value >= 1 && value <= 60000)
                return value;
        }
        return fallback;
    }
}
