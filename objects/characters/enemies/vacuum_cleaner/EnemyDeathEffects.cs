using Godot;

namespace Shapeshift.Enemies;

public static class EnemyDeathEffects
{
	public static void SpawnExplosion(Node3D owner, PackedScene explosionScene, float scale, string nameSuffix, double cleanupSeconds = 4.0)
	{
		if (owner == null || explosionScene == null)
		{
			return;
		}

		Node3D explosion = explosionScene.Instantiate<Node3D>();
		Node parent = owner.GetTree().CurrentScene ?? owner.GetParent();

		if (parent == null)
		{
			explosion.QueueFree();
			return;
		}

		explosion.Name = $"{owner.Name}{nameSuffix}";
		explosion.TopLevel = true;
		explosion.Set("one_shot", true);
		explosion.Set("autoplay", false);

		if (explosion.HasSignal("finished"))
		{
			explosion.Connect("finished", Callable.From(() => explosion.QueueFree()));
		}

		parent.AddChild(explosion);
		explosion.GlobalPosition = owner.GlobalPosition;
		explosion.Scale = Vector3.One * scale;
		DisableExplosionAnimationLoop(explosion);

		if (explosion.HasMethod("play"))
		{
			explosion.Call("play");
		}

		owner.GetTree().CreateTimer(cleanupSeconds).Timeout += () =>
		{
			if (GodotObject.IsInstanceValid(explosion))
			{
				explosion.QueueFree();
			}
		};
	}

	public static void PlaySpatialSound(Node3D owner, AudioStream sound, string nameSuffix, float volumeDb, float maxDistance, double cleanupSeconds = 6.0)
	{
		if (owner == null || sound == null)
		{
			return;
		}

		AudioStreamPlayer3D audio = new()
		{
			Name = $"{owner.Name}{nameSuffix}",
			Stream = sound,
			VolumeDb = volumeDb,
			MaxDistance = maxDistance,
			TopLevel = true
		};

		Node parent = owner.GetTree().CurrentScene ?? owner.GetParent();

		if (parent == null)
		{
			audio.QueueFree();
			return;
		}

		parent.AddChild(audio);
		audio.GlobalPosition = owner.GlobalPosition;
		audio.Finished += () => audio.QueueFree();
		audio.Play();

		owner.GetTree().CreateTimer(cleanupSeconds).Timeout += () =>
		{
			if (GodotObject.IsInstanceValid(audio))
			{
				audio.QueueFree();
			}
		};
	}

	private static void DisableExplosionAnimationLoop(Node3D explosion)
	{
		AnimationPlayer animationPlayer = explosion.GetNodeOrNull<AnimationPlayer>("AnimationPlayer");
		Animation mainAnimation = animationPlayer?.GetAnimation("main");

		if (mainAnimation != null)
		{
			mainAnimation.LoopMode = Animation.LoopModeEnum.None;
		}
	}
}
