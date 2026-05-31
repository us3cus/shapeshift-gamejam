using System.Collections.Generic;
using Godot;

namespace Shapeshift.Enemies.Fanner;

public partial class Enemy : CharacterBody3D
{
	[Export] public NodePath TargetPath { get; set; }
	[Export] public NodePath NavigationAgentPath { get; set; } = "NavigationAgent3D";
	[Export] public float Speed { get; set; } = 4.0f;
	[Export] public float Acceleration { get; set; } = 18.0f;
	[Export] public float Gravity { get; set; } = 24.0f;
	[Export] public float PreferredAttackDistance { get; set; } = 4.6f;
	[Export] public float TargetRefreshSeconds { get; set; } = 0.1f;
	[Export] public float MaxHealth { get; set; } = 35.0f;
	[Export] public NodePath HealthBarRootPath { get; set; } = "HealthBarRoot";
	[Export] public NodePath HealthBarFillPath { get; set; } = "HealthBarRoot/Fill";
	[Export] public PackedScene DeathExplosionScene { get; set; } = GD.Load<PackedScene>("res://addons/ExplosionFXFree/assets/BinbunVFX_Vol2/ExplosionFX/effects/ground/vfx_ground_explosion_01.tscn");
	[Export] public float DeathExplosionScale { get; set; } = 0.6f;
	[Export] public AudioStream DeathSound { get; set; } = GD.Load<AudioStream>("res://objects/characters/enemies/vacuum_cleaner/explode.mp3");
	[Export] public float DeathSoundVolumeDb { get; set; } = 0.0f;
	[Export] public float DeathSoundMaxDistance { get; set; } = 28.0f;
	[Export] public bool UseDirectFallback { get; set; } = true;
	[Export] public float MinPathPointDistance { get; set; } = 0.25f;
	[Export] public bool DebugLogsEnabled { get; set; } = false;
	[Export] public float DebugLogIntervalSeconds { get; set; } = 1.0f;

	[ExportGroup("Flamethrower")]
	[Export] public PackedScene FlameScene { get; set; } = GD.Load<PackedScene>("res://addons/ExplosionFXFree/assets/FlameFXFree/assets/BinbunVFX_Vol2/FlameFX/effects/fire/vfx_basic_fire_01.tscn");
	[Export] public NodePath FlameOriginPath { get; set; } = "FlameOrigin";
	[Export] public Vector3 FallbackFlameOriginLocalPosition { get; set; } = new(0.0f, 0.65f, -0.9f);
	[Export] public float AttackRange { get; set; } = 6.0f;
	[Export] public float AttackConeDegrees { get; set; } = 34.0f;
	[Export] public float DamagePerSecond { get; set; } = 16.0f;
	[Export(PropertyHint.Layers3DPhysics)] public uint AttackCollisionMask { get; set; } = 3;
	[Export] public bool RequireLineOfSight { get; set; } = true;
	[Export] public int FlameSegmentCount { get; set; } = 5;
	[Export] public int FlameParticlesPerSegment { get; set; } = 40;
	[Export] public float FlameStartScale { get; set; } = 0.55f;
	[Export] public float FlameEndScale { get; set; } = 1.35f;

	private const float HealthBarWidth = 1.2f;
	private const float HealthBarHeight = 0.12f;

	private readonly List<Node3D> _flameSegments = new();

	private NavigationAgent3D _navigationAgent;
	private Node3D _healthBarRoot;
	private MeshInstance3D _healthBarFill;
	private Node3D _flameOrigin;
	private Node3D _target;
	private double _targetRefreshTimer;
	private double _debugLogTimer;
	private float _health;
	private bool _isDead;
	private bool _flameActive;
	private bool _warnedAboutTargetPath;
	private bool _warnedAboutMissingTarget;
	private string _lastMoveState = "not initialized";

	public override void _Ready()
	{
		_health = MaxHealth;
		_navigationAgent = ResolveNavigationAgent();
		_healthBarRoot = GetNodeOrNull<Node3D>(HealthBarRootPath);
		_healthBarFill = GetNodeOrNull<MeshInstance3D>(HealthBarFillPath);
		_flameOrigin = ResolveFlameOrigin();

		if (_navigationAgent != null)
		{
			_navigationAgent.PathDesiredDistance = 0.35f;
			_navigationAgent.TargetDesiredDistance = PreferredAttackDistance;
			_navigationAgent.AvoidanceEnabled = false;
		}
		else
		{
			LogError($"NavigationAgent3D not found by path '{NavigationAgentPath}'. Children: {FormatChildren()}");
		}

		ResolveTarget();
		SetupFlameSegments();
		UpdateHealthBar();
		LogDebug($"ready. target={FormatNode(_target)}, nav_agent={FormatNode(_navigationAgent)}, position={FormatVector(GlobalPosition)}");
	}

	public void ApplyDamage(float amount, Node source)
	{
		if (_isDead || amount <= 0.0f)
		{
			return;
		}

		_health = Mathf.Max(0.0f, _health - amount);
		UpdateHealthBar();
		LogDebug($"took {amount:0.00} damage from {FormatNode(source)}; health={_health:0.00}", false);

		if (_health <= 0.0f)
		{
			Die();
		}
	}

	public override void _PhysicsProcess(double delta)
	{
		if (_isDead)
		{
			return;
		}

		if (_target == null || !GodotObject.IsInstanceValid(_target))
		{
			ResolveTarget();
		}

		ApplyGravity(delta);

		bool attacking = false;

		if (_target != null)
		{
			FaceTarget();

			if (CanAttackTarget(out Node damageCollider))
			{
				Decelerate(delta);
				ApplyFlameDamage(damageCollider, delta);
				attacking = true;
				_lastMoveState = "attacking with flamethrower";
			}
			else if (_navigationAgent != null)
			{
				UpdateTargetPosition(delta);
				ChaseTarget(delta);
			}
			else
			{
				Decelerate(delta);
			}
		}
		else
		{
			Decelerate(delta);
		}

		SetFlameActive(attacking);
		MoveAndSlide();
		FaceHealthBarToCamera();
		LogMovementStatus(delta);
	}

	private void ApplyFlameDamage(Node damageCollider, double delta)
	{
		Node receiver = FindDamageReceiver(damageCollider) ?? FindDamageReceiver(_target);

		if (receiver == null)
		{
			return;
		}

		float amount = DamagePerSecond * (float)delta;

		if (receiver.HasMethod("ApplyDamage"))
		{
			receiver.Call("ApplyDamage", amount, this);
		}
		else if (receiver.HasMethod("apply_damage"))
		{
			receiver.Call("apply_damage", amount, this);
		}
		else if (receiver.HasMethod("TakeDamage"))
		{
			receiver.Call("TakeDamage", amount, this);
		}
		else if (receiver.HasMethod("take_damage"))
		{
			receiver.Call("take_damage", amount, this);
		}
	}

	private bool CanAttackTarget(out Node damageCollider)
	{
		damageCollider = null;

		if (_target == null || _flameOrigin == null)
		{
			return false;
		}

		Vector3 origin = _flameOrigin.GlobalPosition;
		Vector3 aimPosition = GetTargetAimPosition();
		Vector3 toTarget = aimPosition - origin;
		float distance = toTarget.Length();

		if (distance > AttackRange || distance < 0.05f)
		{
			return false;
		}

		Vector3 directionToTarget = toTarget / distance;
		Vector3 forward = -_flameOrigin.GlobalTransform.Basis.Z.Normalized();
		float coneCos = Mathf.Cos(Mathf.DegToRad(AttackConeDegrees * 0.5f));

		if (forward.Dot(directionToTarget) < coneCos)
		{
			return false;
		}

		if (!RequireLineOfSight)
		{
			damageCollider = _target;
			return true;
		}

		PhysicsRayQueryParameters3D query = PhysicsRayQueryParameters3D.Create(origin, aimPosition);
		query.CollisionMask = AttackCollisionMask;
		query.CollideWithAreas = true;
		query.CollideWithBodies = true;
		query.Exclude = BuildExclusionRids();

		Godot.Collections.Dictionary result = GetWorld3D().DirectSpaceState.IntersectRay(query);

		if (result.Count == 0)
		{
			damageCollider = _target;
			return true;
		}

		GodotObject colliderObject = result["collider"].AsGodotObject();
		damageCollider = colliderObject as Node;

		return IsPlayerNode(damageCollider);
	}

	private Vector3 GetTargetAimPosition()
	{
		return _target.GlobalPosition + Vector3.Up * 0.85f;
	}

	private void SetupFlameSegments()
	{
		if (FlameScene == null || _flameOrigin == null)
		{
			return;
		}

		int segmentCount = Mathf.Max(1, FlameSegmentCount);

		for (int i = 0; i < segmentCount; i++)
		{
			Node3D flame = FlameScene.Instantiate<Node3D>();
			float progress = segmentCount <= 1 ? 0.0f : i / (float)(segmentCount - 1);
			float localDistance = Mathf.Lerp(0.15f, AttackRange * 0.82f, progress);
			float segmentScale = Mathf.Lerp(FlameStartScale, FlameEndScale, progress);

			flame.Name = $"FlameSegment{i + 1}";
			flame.Position = new Vector3(0.0f, 0.0f, -localDistance);
			flame.RotationDegrees = new Vector3(-90.0f, 0.0f, 0.0f);
			flame.Scale = Vector3.One * segmentScale;
			_flameOrigin.AddChild(flame);

			ConfigureFlameSegment(flame, progress);
			_flameSegments.Add(flame);
		}

		SetFlameActive(false, true);
	}

	private void ConfigureFlameSegment(Node3D flame, float progress)
	{
		flame.Set("emitting", false);
		flame.Set("audio_playing", false);
		flame.Set("particles_amount", FlameParticlesPerSegment);
		flame.Set("lifetime", Mathf.Lerp(0.22f, 0.55f, progress));
		flame.Set("flame_scale", new Vector2(Mathf.Lerp(0.5f, 1.25f, progress), Mathf.Lerp(0.9f, 1.7f, progress)));
		flame.Set("wobble_amount", 0.55f);
		flame.Set("wobble_scroll", 8.0f);
		flame.Set("emission", 7.5f);
		flame.Set("audio_max_distance", AttackRange + 2.0f);
		flame.Visible = false;
	}

	private void SetFlameActive(bool active, bool force = false)
	{
		if (!force && _flameActive == active)
		{
			return;
		}

		_flameActive = active;

		for (int i = 0; i < _flameSegments.Count; i++)
		{
			Node3D flame = _flameSegments[i];

			if (!GodotObject.IsInstanceValid(flame))
			{
				continue;
			}

			flame.Visible = active;
			flame.Set("emitting", active);
			flame.Set("audio_playing", active && i == 0);
		}
	}

	private Node3D ResolveFlameOrigin()
	{
		Node3D origin = null;

		if (FlameOriginPath != null && !FlameOriginPath.IsEmpty)
		{
			origin = GetNodeOrNull<Node3D>(FlameOriginPath);
		}

		if (origin != null)
		{
			return origin;
		}

		origin = new Node3D
		{
			Name = "FlameOrigin",
			Position = FallbackFlameOriginLocalPosition
		};

		AddChild(origin);
		return origin;
	}

	private void UpdateHealthBar()
	{
		if (_healthBarFill == null)
		{
			return;
		}

		float healthRatio = MaxHealth <= 0.0f ? 0.0f : Mathf.Clamp(_health / MaxHealth, 0.0f, 1.0f);
		float fillWidth = HealthBarWidth * healthRatio;

		if (_healthBarFill.Mesh is QuadMesh fillMesh)
		{
			fillMesh.Size = new Vector2(fillWidth, HealthBarHeight);
		}

		_healthBarFill.Position = new Vector3((fillWidth - HealthBarWidth) * 0.5f, 0.0f, -0.002f);
	}

	private void FaceHealthBarToCamera()
	{
		if (_healthBarRoot == null)
		{
			return;
		}

		Camera3D camera = GetViewport().GetCamera3D();

		if (camera == null)
		{
			return;
		}

		Vector3 lookPosition = camera.GlobalPosition;

		if (_healthBarRoot.GlobalPosition.DistanceSquaredTo(lookPosition) > 0.001f)
		{
			_healthBarRoot.LookAt(lookPosition, Vector3.Up);
		}
	}

	private void UpdateTargetPosition(double delta)
	{
		_targetRefreshTimer -= delta;

		if (_targetRefreshTimer > 0.0)
		{
			return;
		}

		_navigationAgent.TargetPosition = _target.GlobalPosition;
		_targetRefreshTimer = TargetRefreshSeconds;
	}

	private void ChaseTarget(double delta)
	{
		Vector3 direction = Vector3.Zero;
		bool usingNavigationPath = false;

		if (!_navigationAgent.IsNavigationFinished())
		{
			Vector3 nextPathPosition = _navigationAgent.GetNextPathPosition();
			direction = nextPathPosition - GlobalPosition;
			direction.Y = 0.0f;
			usingNavigationPath = true;
		}

		if (direction.Length() < MinPathPointDistance)
		{
			if (UseDirectFallback && _target != null)
			{
				direction = _target.GlobalPosition - GlobalPosition;
				direction.Y = 0.0f;
				usingNavigationPath = false;
			}

			if (direction.LengthSquared() < 0.0001f)
			{
				_lastMoveState = "standing: next navigation point and direct target direction are both zero";
				Decelerate(delta);
				return;
			}
		}

		direction = direction.Normalized();
		Vector3 horizontalVelocity = new(Velocity.X, 0.0f, Velocity.Z);
		horizontalVelocity = horizontalVelocity.MoveToward(direction * Speed, Acceleration * (float)delta);

		Velocity = new Vector3(horizontalVelocity.X, Velocity.Y, horizontalVelocity.Z);
		FaceTarget();
		_lastMoveState = usingNavigationPath ? "moving by NavigationAgent3D path" : "moving by direct fallback";
	}

	private void ApplyGravity(double delta)
	{
		Vector3 velocity = Velocity;

		if (!IsOnFloor())
		{
			velocity.Y -= Gravity * (float)delta;
		}
		else if (velocity.Y < 0.0f)
		{
			velocity.Y = 0.0f;
		}

		Velocity = velocity;
	}

	private void Decelerate(double delta)
	{
		Vector3 horizontalVelocity = new(Velocity.X, 0.0f, Velocity.Z);
		horizontalVelocity = horizontalVelocity.MoveToward(Vector3.Zero, Acceleration * (float)delta);

		Velocity = new Vector3(horizontalVelocity.X, Velocity.Y, horizontalVelocity.Z);
	}

	private void FaceTarget()
	{
		if (_target == null)
		{
			return;
		}

		Vector3 lookPosition = _target.GlobalPosition;
		lookPosition.Y = GlobalPosition.Y;

		if (GlobalPosition.DistanceSquaredTo(lookPosition) > 0.001f)
		{
			LookAt(lookPosition, Vector3.Up);
		}
	}

	private void ResolveTarget()
	{
		_target = null;

		if (TargetPath != null && !TargetPath.IsEmpty)
		{
			Node3D explicitTarget = GetNodeOrNull<Node3D>(TargetPath);

			if (explicitTarget != null && IsPlayerNode(explicitTarget))
			{
				_target = explicitTarget;
			}
			else if (explicitTarget != null && !_warnedAboutTargetPath)
			{
				GD.PushWarning($"{Name}: TargetPath points to '{explicitTarget.Name}', but the target must be in the 'player' group. Falling back to the first player node.");
				_warnedAboutTargetPath = true;
			}
		}

		if (_target == null)
		{
			_target = GetTree().GetFirstNodeInGroup("player") as Node3D;
		}

		if (_target == null && !_warnedAboutMissingTarget)
		{
			LogError("player target was not found. Put the character in the 'player' group or set TargetPath to the character node.");
			_warnedAboutMissingTarget = true;
		}
	}

	private NavigationAgent3D ResolveNavigationAgent()
	{
		NavigationAgent3D agent = null;

		if (NavigationAgentPath != null && !NavigationAgentPath.IsEmpty)
		{
			agent = GetNodeOrNull<NavigationAgent3D>(NavigationAgentPath);
		}

		if (agent == null)
		{
			agent = FindChild("NavigationAgent3D", true, false) as NavigationAgent3D;
		}

		if (agent == null)
		{
			foreach (Node child in GetChildren())
			{
				if (child is NavigationAgent3D childAgent)
				{
					agent = childAgent;
					break;
				}
			}
		}

		return agent;
	}

	private Godot.Collections.Array<Rid> BuildExclusionRids()
	{
		Godot.Collections.Array<Rid> exclusions = new();

		if (this is CollisionObject3D selfCollision)
		{
			exclusions.Add(selfCollision.GetRid());
		}

		return exclusions;
	}

	private static Node FindDamageReceiver(Node node)
	{
		Node current = node;

		while (current != null)
		{
			if (current.HasMethod("ApplyDamage") ||
				current.HasMethod("apply_damage") ||
				current.HasMethod("TakeDamage") ||
				current.HasMethod("take_damage"))
			{
				return current;
			}

			current = current.GetParent();
		}

		return null;
	}

	private static bool IsPlayerNode(Node node)
	{
		Node current = node;

		while (current != null)
		{
			if (current.IsInGroup("player"))
			{
				return true;
			}

			current = current.GetParent();
		}

		return false;
	}

	private void Die()
	{
		if (_isDead)
		{
			return;
		}

		_isDead = true;
		SetFlameActive(false, true);
		LogDebug("destroyed");
		SpawnDeathSound();
		SpawnDeathExplosion();
		QueueFree();
	}

	private void SpawnDeathSound()
	{
		Shapeshift.Enemies.EnemyDeathEffects.PlaySpatialSound(this, DeathSound, "DeathSound", DeathSoundVolumeDb, DeathSoundMaxDistance);
	}

	private void SpawnDeathExplosion()
	{
		Shapeshift.Enemies.EnemyDeathEffects.SpawnExplosion(this, DeathExplosionScene, DeathExplosionScale, "DeathExplosion");
	}

	private void LogMovementStatus(double delta)
	{
		if (!DebugLogsEnabled)
		{
			return;
		}

		_debugLogTimer -= delta;

		if (_debugLogTimer > 0.0)
		{
			return;
		}

		_debugLogTimer = DebugLogIntervalSeconds;

		if (_target == null)
		{
			LogError("not moving: target is null");
			return;
		}

		float distanceToTarget = GlobalPosition.DistanceTo(_target.GlobalPosition);
		float horizontalSpeed = new Vector2(Velocity.X, Velocity.Z).Length();

		LogDebug(
			$"state={_lastMoveState}; " +
			$"pos={FormatVector(GlobalPosition)}; " +
			$"target={FormatNode(_target)} at {FormatVector(_target.GlobalPosition)}; " +
			$"distance={distanceToTarget:0.00}; " +
			$"velocity={FormatVector(Velocity)}; horizontal_speed={horizontalSpeed:0.00}; " +
			$"flame_active={_flameActive}"
		);
	}

	private void LogDebug(string message, bool force = true)
	{
		if (!DebugLogsEnabled && !force)
		{
			return;
		}

		GD.Print($"[Fanner:{Name}] {message}");
	}

	private void LogError(string message)
	{
		GD.PushError($"[Fanner:{Name}] {message}");
	}

	private string FormatChildren()
	{
		string result = "";

		foreach (Node child in GetChildren())
		{
			if (result.Length > 0)
			{
				result += ", ";
			}

			result += $"{child.Name}:{child.GetClass()}";
		}

		return result.Length == 0 ? "no children" : result;
	}

	private static string FormatNode(Node node)
	{
		return node == null ? "null" : $"{node.Name} ({node.GetPath()})";
	}

	private static string FormatVector(Vector3 value)
	{
		return $"({value.X:0.00}, {value.Y:0.00}, {value.Z:0.00})";
	}
}
