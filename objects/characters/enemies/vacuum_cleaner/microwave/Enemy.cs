using Godot;

namespace Shapeshift.Enemies.Microwave;

public partial class Enemy : CharacterBody3D
{
	[Export] public NodePath TargetPath { get; set; }
	[Export] public NodePath NavigationAgentPath { get; set; } = "NavigationAgent3D";
	[Export] public NodePath TouchAreaPath { get; set; } = "TouchArea";
	[Export] public float Speed { get; set; } = 3.9f;
	[Export] public float Acceleration { get; set; } = 18.0f;
	[Export] public float Gravity { get; set; } = 24.0f;
	[Export] public float PreferredAttackDistance { get; set; } = 5.5f;
	[Export] public float AttackRange { get; set; } = 8.5f;
	[Export] public float TargetRefreshSeconds { get; set; } = 0.1f;
	[Export] public float MaxHealth { get; set; } = 32.0f;
	[Export] public NodePath HealthBarRootPath { get; set; } = "HealthBarRoot";
	[Export] public NodePath HealthBarFillPath { get; set; } = "HealthBarRoot/Fill";
	[Export] public PackedScene DeathExplosionScene { get; set; } = GD.Load<PackedScene>("res://addons/ExplosionFXFree/assets/BinbunVFX_Vol2/ExplosionFX/effects/ground/vfx_ground_explosion_01.tscn");
	[Export] public float DeathExplosionScale { get; set; } = 0.75f;
	[Export] public AudioStream DeathSound { get; set; } = GD.Load<AudioStream>("res://objects/characters/enemies/vacuum_cleaner/explode.mp3");
	[Export] public float DeathSoundVolumeDb { get; set; } = 0.0f;
	[Export] public float DeathSoundMaxDistance { get; set; } = 28.0f;
	[Export] public bool UseDirectFallback { get; set; } = true;
	[Export] public float MinPathPointDistance { get; set; } = 0.25f;
	[Export] public bool DebugLogsEnabled { get; set; } = false;
	[Export] public float DebugLogIntervalSeconds { get; set; } = 1.0f;

	[ExportGroup("Electric Attack")]
	[Export] public PackedScene ZapScene { get; set; } = GD.Load<PackedScene>("res://addons/BinbunVFX/ElectricFXFree/assets/BinbunVFX_Vol2/ElectricFX/effects/zap/vfx_zap_lightning_01.tscn");
	[Export] public NodePath LightningOriginPath { get; set; } = "LightningOrigin";
	[Export] public Vector3 FallbackLightningOriginLocalPosition { get; set; } = new(0.0f, 0.95f, -0.25f);
	[Export] public float ShockDamage { get; set; } = 18.0f;
	[Export] public float ShockCooldownSeconds { get; set; } = 1.15f;
	[Export] public float FirstShockDelaySeconds { get; set; } = 0.25f;
	[Export] public float TargetAimHeight { get; set; } = 0.85f;
	[Export] public float MinimumZapLength { get; set; } = 0.35f;
	[Export] public float ZapSpeedScale { get; set; } = 1.35f;
	[Export] public double ZapCleanupSeconds { get; set; } = 1.4;
	[Export] public Color ZapPrimaryColor { get; set; } = new(1.0f, 1.0f, 1.0f, 1.0f);
	[Export] public Color ZapSecondaryColor { get; set; } = new(0.0f, 0.84f, 1.0f, 1.0f);
	[Export] public float ZapEmission { get; set; } = 4.5f;
	[Export] public bool RequireLineOfSight { get; set; } = true;
	[Export(PropertyHint.Layers3DPhysics)] public uint AttackCollisionMask { get; set; } = 3;

	private const float HealthBarWidth = 1.2f;
	private const float HealthBarHeight = 0.12f;

	private NavigationAgent3D _navigationAgent;
	private Area3D _touchArea;
	private Node3D _healthBarRoot;
	private MeshInstance3D _healthBarFill;
	private Node3D _lightningOrigin;
	private Node3D _target;
	private double _targetRefreshTimer;
	private double _debugLogTimer;
	private double _shockCooldownTimer;
	private float _health;
	private bool _isDead;
	private bool _warnedAboutTargetPath;
	private bool _warnedAboutMissingTarget;
	private string _lastMoveState = "not initialized";

	public override void _Ready()
	{
		_health = MaxHealth;
		_shockCooldownTimer = FirstShockDelaySeconds;
		_navigationAgent = ResolveNavigationAgent();
		_touchArea = GetNodeOrNull<Area3D>(TouchAreaPath);
		_healthBarRoot = GetNodeOrNull<Node3D>(HealthBarRootPath);
		_healthBarFill = GetNodeOrNull<MeshInstance3D>(HealthBarFillPath);
		_lightningOrigin = ResolveLightningOrigin();

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

		if (_touchArea != null)
		{
			_touchArea.BodyEntered += OnTouchAreaBodyEntered;
		}
		else
		{
			LogError($"Touch Area3D not found by path '{TouchAreaPath}'. Microwave will use ray/distance attacks only.");
		}

		ResolveTarget();
		UpdateHealthBar();
		LogDebug($"ready. target={FormatNode(_target)}, lightning_origin={FormatNode(_lightningOrigin)}, nav_agent={FormatNode(_navigationAgent)}");
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

		_shockCooldownTimer = Mathf.Max(0.0, _shockCooldownTimer - delta);
		ApplyGravity(delta);

		if (_target != null)
		{
			FaceTarget();

			if (CanShockTarget(out Node damageCollider, out Vector3 impactPosition))
			{
				Decelerate(delta);
				TryShock(damageCollider, impactPosition);
				_lastMoveState = "attacking with electricity";
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

		MoveAndSlide();
		ShockCollidingPlayer();
		FaceHealthBarToCamera();
		LogMovementStatus(delta);
	}

	private bool CanShockTarget(out Node damageCollider, out Vector3 impactPosition)
	{
		damageCollider = null;
		impactPosition = Vector3.Zero;

		if (_target == null || _lightningOrigin == null)
		{
			return false;
		}

		Vector3 origin = _lightningOrigin.GlobalPosition;
		Vector3 aimPosition = GetTargetAimPosition();
		float distance = origin.DistanceTo(aimPosition);

		if (distance > AttackRange || distance < 0.05f)
		{
			return false;
		}

		if (!RequireLineOfSight)
		{
			damageCollider = _target;
			impactPosition = aimPosition;
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
			impactPosition = aimPosition;
			return true;
		}

		GodotObject colliderObject = result["collider"].AsGodotObject();
		damageCollider = colliderObject as Node;
		impactPosition = result["position"].AsVector3();

		return IsPlayerNode(damageCollider);
	}

	private void TryShock(Node damageCollider, Vector3 impactPosition)
	{
		if (_shockCooldownTimer > 0.0 || _lightningOrigin == null)
		{
			return;
		}

		Node receiver = FindDamageReceiver(damageCollider) ?? FindDamageReceiver(_target);

		if (receiver != null)
		{
			ApplyShockDamage(receiver);
		}

		SpawnElectricZap(impactPosition);
		_shockCooldownTimer = ShockCooldownSeconds;
		LogDebug($"shock fired at {FormatVector(impactPosition)}", false);
	}

	private void ApplyShockDamage(Node receiver)
	{
		if (ShockDamage <= 0.0f)
		{
			return;
		}

		if (receiver.HasMethod("ApplyDamage"))
		{
			receiver.Call("ApplyDamage", ShockDamage, this);
		}
		else if (receiver.HasMethod("apply_damage"))
		{
			receiver.Call("apply_damage", ShockDamage, this);
		}
		else if (receiver.HasMethod("TakeDamage"))
		{
			receiver.Call("TakeDamage", ShockDamage, this);
		}
		else if (receiver.HasMethod("take_damage"))
		{
			receiver.Call("take_damage", ShockDamage, this);
		}
	}

	private void SpawnElectricZap(Vector3 impactPosition)
	{
		if (ZapScene == null || _lightningOrigin == null)
		{
			return;
		}

		Node parent = GetTree().CurrentScene ?? GetParent();

		if (parent == null)
		{
			return;
		}

		Node3D zap = ZapScene.Instantiate<Node3D>();
		Vector3 origin = _lightningOrigin.GlobalPosition;
		Vector3 toOrigin = origin - impactPosition;
		float zapLength = toOrigin.Length();

		if (zapLength < MinimumZapLength)
		{
			toOrigin = -GlobalTransform.Basis.Z.Normalized() * Mathf.Max(0.05f, MinimumZapLength);
			zapLength = toOrigin.Length();
		}

		zap.Name = $"{Name}ElectricZap";
		zap.TopLevel = true;
		parent.AddChild(zap);
		zap.GlobalPosition = impactPosition;
		zap.GlobalTransform = new Transform3D(BuildBasisFromYAxis(toOrigin), impactPosition);
		zap.Set("height", zapLength);
		zap.Set("one_shot", true);
		zap.Set("autoplay", false);
		zap.Set("speed_scale", ZapSpeedScale);
		zap.Set("primary_color", ZapPrimaryColor);
		zap.Set("secondary_color", ZapSecondaryColor);
		zap.Set("light_color", ZapSecondaryColor);
		zap.Set("emission", ZapEmission);

		if (zap.HasSignal("finished"))
		{
			zap.Connect("finished", Callable.From(() => zap.QueueFree()));
		}

		if (zap.HasMethod("play"))
		{
			zap.Call("play");
		}

		GetTree().CreateTimer(ZapCleanupSeconds).Timeout += () =>
		{
			if (GodotObject.IsInstanceValid(zap))
			{
				zap.QueueFree();
			}
		};
	}

	private void ShockCollidingPlayer()
	{
		if (_shockCooldownTimer > 0.0)
		{
			return;
		}

		for (int i = 0; i < GetSlideCollisionCount(); i++)
		{
			KinematicCollision3D collision = GetSlideCollision(i);

			if (collision.GetCollider() is Node collider && IsPlayerNode(collider))
			{
				Vector3 impactPosition = collision.GetPosition();
				TryShock(collider, impactPosition);
				return;
			}
		}
	}

	private void OnTouchAreaBodyEntered(Node3D body)
	{
		if (!IsPlayerNode(body))
		{
			return;
		}

		TryShock(body, body.GlobalPosition + Vector3.Up * TargetAimHeight);
	}

	private Vector3 GetTargetAimPosition()
	{
		return _target.GlobalPosition + Vector3.Up * TargetAimHeight;
	}

	private Node3D ResolveLightningOrigin()
	{
		Node3D origin = null;

		if (LightningOriginPath != null && !LightningOriginPath.IsEmpty)
		{
			origin = GetNodeOrNull<Node3D>(LightningOriginPath);
		}

		if (origin != null)
		{
			return origin;
		}

		origin = new Node3D
		{
			Name = "LightningOrigin",
			Position = FallbackLightningOriginLocalPosition
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
		AddCollisionExclusions(this, exclusions);
		return exclusions;
	}

	private static void AddCollisionExclusions(Node node, Godot.Collections.Array<Rid> exclusions)
	{
		if (node is CollisionObject3D collisionObject)
		{
			exclusions.Add(collisionObject.GetRid());
		}

		foreach (Node child in node.GetChildren())
		{
			AddCollisionExclusions(child, exclusions);
		}
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
		SetPhysicsProcess(false);
		Velocity = Vector3.Zero;
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
			$"shock_cooldown={_shockCooldownTimer:0.00}"
		);
	}

	private void LogDebug(string message, bool force = true)
	{
		if (!DebugLogsEnabled && !force)
		{
			return;
		}

		GD.Print($"[Microwave:{Name}] {message}");
	}

	private void LogError(string message)
	{
		GD.PushError($"[Microwave:{Name}] {message}");
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

	private static Basis BuildBasisFromYAxis(Vector3 yAxis)
	{
		if (yAxis.LengthSquared() < 0.0001f)
		{
			return Basis.Identity;
		}

		yAxis = yAxis.Normalized();
		Vector3 reference = Mathf.Abs(yAxis.Dot(Vector3.Up)) > 0.95f ? new Vector3(0.0f, 0.0f, -1.0f) : Vector3.Up;
		Vector3 xAxis = reference.Cross(yAxis).Normalized();

		if (xAxis.LengthSquared() < 0.0001f)
		{
			xAxis = Vector3.Right;
		}

		Vector3 zAxis = xAxis.Cross(yAxis).Normalized();
		return new Basis(xAxis, yAxis, zAxis);
	}
}
