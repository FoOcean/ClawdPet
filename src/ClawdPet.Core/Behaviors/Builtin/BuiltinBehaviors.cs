namespace ClawdPet.Core.Behaviors.Builtin;

/// <summary>
/// The default cast of behaviors. To add one: write a <see cref="PetBehavior"/> (or reuse
/// <see cref="ClipBehavior"/> for a play-once animation), then register it here — or call
/// <see cref="BehaviorRegistry.Register"/> on <c>PetEngine.Context.Registry</c> from a host/plugin.
/// </summary>
public static class BuiltinBehaviors
{
    public static BehaviorRegistry CreateRegistry() => new BehaviorRegistry()
        .Register(new("idle", () => new IdleBehavior()) { AutoWeight = _ => 2.5 })
        .Register(new("walk", () => new WalkBehavior()) { MenuLabel = "散步", AutoWeight = _ => 4, Aliases = ["wander"] })
        .Register(new("work", () => new WorkBehavior())
        {
            MenuLabel = "敲代码", AutoWeight = _ => 1.5, Aliases = ["typing", "type", "code", "laptop"],
        })
        .Register(new("think", () => new ThinkBehavior()) { MenuLabel = "思考", AutoWeight = _ => 1, Aliases = ["thinking"] })
        .Register(new("look", () => new ClipBehavior("look")) { MenuLabel = "东张西望", AutoWeight = _ => 2 })
        .Register(new("wave", () => new ClipBehavior("wave")) { MenuLabel = "打招呼", AutoWeight = _ => 0.6, Aliases = ["hi", "hello"] })
        .Register(new("happy", () => new ClipBehavior("happy", swallowClicks: true)) { MenuLabel = "开心", AutoWeight = _ => 0.5, Aliases = ["jump", "yay"] })
        .Register(new("surprised", () => new ClipBehavior("surprised")) { MenuLabel = "惊讶", AutoWeight = _ => 0.2, Aliases = ["wow"] })
        .Register(new("sleep", () => new SleepBehavior())
        {
            MenuLabel = "睡觉",
            AutoWeight = ctx => ctx.SinceInteractionMs > ctx.Options.SleepAfterMs ? 4 : 0.1,
            Aliases = ["nap"],
        })
        .Register(new("drag", () => new DragBehavior()))
        .Register(new("fall", () => new FallBehavior()))
        .Register(new("play", () => new ClipBehavior()));
}
