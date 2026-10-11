using ClawdPet.Core;
using ClawdPet.Core.Animation;
using ClawdPet.Core.Behaviors;
using ClawdPet.Core.Sprites;
using ClawdPet.Core.World;

namespace ClawdPet.Core.Tests;

sealed class FakeWorld : IPetWorld
{
    public Surface Surface { get; set; } = new(0, 0, 1920, 1040);
    public Surface SurfaceAt(double x, double y) => Surface;
}

public class PetEngineTests
{
    static SpriteAtlas Atlas() => SpriteAtlas.Load(Path.Combine(AppContext.BaseDirectory, "skin", "clawd.json"));

    static PetEngine NewPet(bool autonomous = false, double x = 960)
    {
        var e = new PetEngine(Atlas(), new FakeWorld(), new PetOptions { Autonomous = autonomous });
        e.Body.Scale = 3;
        e.Body.X = x;
        e.Body.Y = 1040;
        e.Start("idle");
        e.Tick(16);
        return e;
    }

    static void Run(PetEngine e, double ms, Action? each = null)
    {
        for (double t = 0; t < ms; t += 16)
        {
            each?.Invoke();
            e.Tick(16);
        }
    }

    static string? Behavior(PetEngine e) => e.Brain.Current?.Name;
    static string? Clip(PetEngine e) => e.Animator.Clip?.Name;

    [Fact]
    public void Atlas_has_every_animation_and_the_ground_anchor()
    {
        var atlas = Atlas();
        Assert.True(File.Exists(atlas.ImagePath));
        Assert.Equal((64, 48), (atlas.FrameWidth, atlas.FrameHeight));
        Assert.Equal(new PixelPoint(32, 46), atlas.Anchor);
        string[] expected =
        [
            "idle", "blink", "look", "walk_right", "walk_left", "happy", "wave", "think",
            "laptop_out", "typing", "laptop_away", "surprised", "drag", "fall", "land",
            "sleep_start", "sleep", "wake",
        ];
        Assert.All(expected, name => Assert.True(atlas.HasClip(name), name));
    }

    [Fact]
    public void Animator_honours_frame_durations_and_completes_once()
    {
        var animator = new Animator(Atlas());
        int completed = 0;
        animator.Completed += _ => completed++;
        animator.Play("blink", loop: false);       // 50, 80, 50 ms

        animator.Update(49);
        Assert.Equal(0, animator.FrameIndex);
        animator.Update(2);
        Assert.Equal(1, animator.FrameIndex);
        animator.Update(200);
        Assert.True(animator.IsFinished);
        animator.Update(200);
        Assert.Equal(1, completed);
    }

    [Fact]
    public void Pingpong_tags_expand_without_repeating_the_ends()
    {
        var frames = Enumerable.Range(0, 4).Select(i => new AtlasFrame($"{i}", default, default, 100)).ToList();
        var clip = AnimationClip.FromRange("x", frames, 0, 3, "pingpong");
        Assert.Equal([0, 1, 2, 3, 2, 1], clip.Frames.Select(f => f.AtlasIndex));
    }

    [Fact]
    public void Click_hops_then_settles_back_to_idle()
    {
        var pet = NewPet();
        pet.Input(PetInputKind.Click);
        pet.Tick(16);
        Assert.Equal("happy", Behavior(pet));
        Run(pet, 2000);
        Assert.Equal("idle", Behavior(pet));
        Assert.Equal(1040, pet.Body.Y);
    }

    [Fact]
    public void Drag_follows_the_cursor_and_a_flick_throws_it()
    {
        var pet = NewPet();
        var ptr = pet.Context.Pointer;
        (ptr.X, ptr.Y, ptr.IsDown) = (960, 1000, true);
        pet.Input(PetInputKind.DragStart);
        pet.Tick(16);
        Assert.Equal("drag", Behavior(pet));

        // Pull up and to the left quickly.
        Run(pet, 160, () => { ptr.X -= 12; ptr.Y -= 20; });
        Assert.Equal(ptr.X, pet.Body.X, 1);
        Assert.True(pet.Body.Y < 900);

        ptr.IsDown = false;
        pet.Input(PetInputKind.DragEnd);
        pet.Tick(16);
        Assert.Equal("fall", Behavior(pet));
        Assert.True(pet.Body.Vx < 0 && pet.Body.Vy < 0, $"thrown up-left, v=({pet.Body.Vx:0},{pet.Body.Vy:0})");

        Run(pet, 4000);
        Assert.Equal("idle", Behavior(pet));
        Assert.Equal(1040, pet.Body.Y);
        Assert.InRange(pet.Body.X, pet.Body.HalfWidth, 1920 - pet.Body.HalfWidth);
    }

    [Fact]
    public void A_grounded_behavior_in_mid_air_falls_and_lands()
    {
        var pet = NewPet();
        pet.Body.Y = 400;
        pet.Tick(16);
        pet.Tick(16);
        Assert.Equal("fall", Behavior(pet));
        Run(pet, 3000);
        Assert.Equal(1040, pet.Body.Y);
        Assert.Equal("idle", Behavior(pet));
    }

    [Fact]
    public void Work_command_types_until_done_then_hops()
    {
        var pet = NewPet();
        Assert.Equal("ok", pet.Command("work"));
        Run(pet, 1500);
        Assert.Equal("work", Behavior(pet));
        Assert.Equal("typing", Clip(pet));

        Run(pet, 20_000);
        Assert.Equal("typing", Clip(pet));   // commanded work lasts until told otherwise

        Assert.Equal("ok", pet.Command("done"));
        pet.Tick(16);
        Assert.Equal("laptop_away", Clip(pet));
        Run(pet, 1200);
        Assert.Equal("happy", Behavior(pet));
        Run(pet, 1500);
        Assert.Equal("idle", Behavior(pet));
    }

    [Fact]
    public void Work_near_the_left_edge_steps_right_to_make_room_for_the_laptop()
    {
        var pet = NewPet(x: 60);
        pet.Command("work 10");
        pet.Tick(16);
        pet.Tick(16);
        Assert.Equal("walk", Behavior(pet));
        Run(pet, 3000);
        Assert.Equal("work", Behavior(pet));
        Assert.True(pet.Body.X - 30 * pet.Body.Scale >= 0, $"laptop fits, x={pet.Body.X}");
    }

    [Fact]
    public void Walking_never_leaves_the_surface()
    {
        var pet = NewPet(autonomous: true, x: 1850);
        pet.Brain.Request("walk", new BehaviorArgs { Direction = 1, DistanceArt = 500 }, force: true);
        Run(pet, 5000);
        Assert.InRange(pet.Body.X, pet.Body.HalfWidth, 1920 - pet.Body.HalfWidth);
    }

    [Fact]
    public void Sleeping_pet_wakes_on_click()
    {
        var pet = NewPet();
        pet.Command("sleep");
        Run(pet, 2500);
        Assert.Equal("sleep", Clip(pet));
        pet.Input(PetInputKind.Click);
        pet.Tick(16);
        Assert.Equal("wake", Clip(pet));
        Run(pet, 2000);
        Assert.Equal("idle", Behavior(pet));
    }

    [Fact]
    public void Commands_report_errors_and_play_any_tag()
    {
        var pet = NewPet();
        Assert.StartsWith("error", pet.Command("bogus"));
        Assert.StartsWith("error", pet.Command("play nope"));
        Assert.Equal("ok", pet.Command("play look"));
        pet.Tick(16);
        Assert.Equal("look", Clip(pet));
        Assert.Equal("ok", pet.Command("typing 2"));   // alias of work
        pet.Tick(16);
        Assert.Equal("work", Behavior(pet));
    }

    [Fact]
    public void A_command_during_the_entrance_drop_runs_after_landing()
    {
        var pet = NewPet();
        pet.Body.Y = 600;
        pet.Start("fall", new BehaviorArgs { Then = "wave" });
        pet.Command("work");
        Run(pet, 3000);
        Assert.Equal("work", Behavior(pet));
    }

    [Fact]
    public void Commands_wait_while_the_pet_is_held()
    {
        var pet = NewPet();
        pet.Context.Pointer.IsDown = true;
        pet.Input(PetInputKind.DragStart);
        pet.Tick(16);
        pet.Command("wave");
        Run(pet, 200);
        Assert.Equal("drag", Behavior(pet));

        pet.Context.Pointer.IsDown = false;
        Run(pet, 3000, () => { });
        Assert.Contains(Behavior(pet), new[] { "wave", "idle" });
    }
}
