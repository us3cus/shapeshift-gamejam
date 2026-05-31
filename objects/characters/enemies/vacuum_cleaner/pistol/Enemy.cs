using Godot;
using Shapeshift.Weapons.Projectiles;

namespace Shapeshift.Enemies.Pistol;

public partial class Enemy : CharacterBody3D
{
	[Export] public NodePath TargetPath { get; set; }
	[Export] public NodePath NavigationAgentPath { get; set; } = "NavigationAgent3D";
	[Export] public float Speed { get; set; } = 3.8f;
	[Export] public float Acceleration { get; set; } = 18.0f;
	[Export] public float Gravity { get; set; } = 24.0f;
	[Export] public float PreferredAttackDistance { get; set; } = 7.0f;
	[Export] public float AttackRange { get; set; } = 13.0f;
	[Export] public float TargetRefreshSeconds { get; set; } = 0.1f;
	[Export] public float MaxHealth { get; set; } = 30.0f;
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

	[ExportGroup("Pistol")]
	[Export] public PackedScene ProjectileScene { get; set; } = GD.Load<PackedScene>("res://objects/weapons/projectiles/round_projectile.tscn");
	[Export] public NodePath MuzzlePath { get; set; } = "Muzzle";
	[Export] public Vector3 FallbackMuzzleLocalPosition { get; set; } = new(0.0f, 0.65f, -0.9f);
	[Export] public float ShootCooldownSeconds { get; set; } = 1.25f;
	[Export] public float FirstShotDelaySeconds { get; set; } = 0.25f;
	[Export] public float TargetAimHeight { get; set; } = 0.85f;
	[Export] public float ProjectileSpawnForwardOffset { get; set; } = 0.08f;
	[Export] public bool RequireLineOfSight { get; set; } = true;
	[Export] public bool UseBallisticAim { get; set; } = true;
	[Export(PropertyHint.Layers3DPhysics)] public uint AttackCollisionMask { get; set; } = 3;

	[ExportGroup("Projectile Flight")]
	[Export] public float ProjectileSpeed { get; set; } = 22.0f;
	[Export] public float ProjectileGravity { get; set; } = 0.0f;
	[Export] public float ProjectileDamage { get; set; } = 12.0f;
	[Export] public float ProjectileLifeSeconds { get; set; } = 4.0f;
	[Export] public float ProjectileMaxDistance { get; set; } = 45.0f;
	[Export(PropertyHint.Layers3DPhysics)] public uint ProjectileHitMask { get; set; } = 3;

	private const float HealthBarWidth = 1.2f;
	private const float HealthBarHeight = 0.12f;

	private NavigationAgent3D _navigationAgent;
	private Node3D _healthBarRoot;
	private MeshInstance3D _healthBarFill;
	private Node3D _muzzle;
	private Node3D _target;
	private double _targetRefreshTimer;
	private double _debugLogTimer;
	private double _shootCooldownTimer;
	private float _health;
	private bool _isDead;
	private bool _warnedAboutTargetPath;
	private bool _warnedAboutMissingTarget;
	private string _lastMoveState = "not initialized";

	public override void _Ready()
	{
		_health = MaxHealth;
		_shootCooldownTimer = FirstShotDelaySeconds;
		_navigationAgent = ResolveNavigationAgent();
		_healthBarRoot = GetNodeOrNull<Node3D>(HealthBarRootPath);
		_healthBarFill = GetNodeOrNull<MeshInstance3D>(HealthBarFillPath);
		_muzzle = ResolveMuzzle();

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
		UpdateHealthBar();
		LogDebug($"ready. target={FormatNode(_target)}, muzzle={FormatNode(_muzzle)}, nav_agent={FormatNode(_navigationAgent)}");
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

		_shootCooldownTimer = Mathf.Max(0.0, _shootCooldownTimer - delta);
		ApplyGravity(delta);

		if (_target != null)
		{
			FaceTarget();

			if (CanShootTarget())
			{
				Decelerate(delta);
				TryShoot();
				_lastMoveState = "attacking with pistol";
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
		FaceHealthBarToCamera();
		LogMovementStatus(delta);
	}

	private bool CanShootTarget()
	{
		if (_target == null || _muzzle == null)
		{
			return false;
		}

		Vector3 origin = _muzzle.GlobalPosition;
		Vector3 aimPosition = GetTargetAimPosition();

		if (origin.DistanceTo(aimPosition) > AttackRange)
		{
			return false;
		}

		if (!RequireLineOfSight)
		{
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
			return true;
		}

		GodotObject colliderObject = result["collider"].AsGodotObject();
		return IsPlayerNode(colliderObject as Node);
	}

	private void TryShoot()
	{
		if (_shootCooldownTimer > 0.0 || ProjectileScene == null || _muzzle == null || _target == null)
		{
			return;
		}

		Node parent = GetTree().CurrentScene ?? GetParent();

		if (parent == null)
		{
			return;
		}

		Vector3 origin = _muzzle.GlobalPosition;
		Vector3 direction = CalculateLaunchDirection(origin, GetTargetAimPosition());

		if (direction.LengthSquared() < 0.0001f)
		{
			return;
		}

		Node3D projectile = ProjectileScene.Instantiate<Node3D>();
		projectile.Name = $"{Name}RoundProjectile";
		projectile.TopLevel = true;
		parent.AddChild(projectile);
		projectile.GlobalPosition = origin + direction * ProjectileSpawnForwardOffset;
		OrientNode(projectile, direction);
		ConfigureProjectile(projectile, direction);

		_shootCooldownTimer = ShootCooldownSeconds;
		LogDebug($"shot projectile from {FormatVector(origin)} toward {FormatVector(direction)}", false);
	}

	private Vector3 CalculateLaunchDirection(Vector3 origin, Vector3 targetPosition)
	{
		Vector3 direct = targetPosition - origin;

		if (direct.LengthSquared() < 0.0001f)
		{
			return -_muzzle.GlobalTransform.Basis.Z.Normalized();
		}

		if (!UseBallisticAim || ProjectileGravity <= 0.0f || ProjectileSpeed <= 0.0f)
		{
			return direct.Normalized();
		}

		Vector3 horizontal = new(direct.X, 0.0f, direct.Z);
		float horizontalDistance = horizontal.Length();

		if (horizontalDistance < 0.001f)
		{
			return direct.Normalized();
		}

		float speedSquared = ProjectileSpeed * ProjectileSpeed;
		float gravity = ProjectileGravity;
		float verticalDistance = direct.Y;
		float discriminant = speedSquared * speedSquared - gravity * (gravity * horizontalDistance * horizontalDistance + 2.0f * verticalDistance * speedSquared);

		if (discriminant < 0.0f)
		{
			return direct.Normalized();
		}

		float tanLowArc = (speedSquared - Mathf.Sqrt(discriminant)) / (gravity * horizontalDistance);
		Vector3 direction = horizontal.Normalized() + Vector3.Up * tanLowArc;
		return direction.Normalized();
	}

	private void ConfigureProjectile(Node3D projectile, Vector3 direction)
	{
		if (projectile is RoundProjectile roundProjectile)
		{
			roundProjectile.Configure(
				ProjectileSpeed,
				ProjectileGravity,
				ProjectileDamage,
				ProjectileLifeSeconds,
				ProjectileMaxDistance,
				ProjectileHitMask
			);
			roundProjectile.Launch(direction, this);
			return;
		}

		projectile.Set("Speed", ProjectileSpeed);
		projectile.Set("ProjectileGravity", ProjectileGravity);
		projectile.Set("Damage", ProjectileDamage);
		projectile.Set("LifeSeconds", ProjectileLifeSeconds);
		projectile.Set("MaxDistance", ProjectileMaxDistance);
		projectile.Set("HitMask", ProjectileHitMask);

		if (projectile.HasMethod("Launch"))
		{
			projectile.Call("Launch", direction, this);
		}
	}

	private Node3D ResolveMuzzle()
	{
		Node3D muzzle = null;

		if (MuzzlePath != null && !MuzzlePath.IsEmpty)
		{
			muzzle = GetNodeOrNull<Node3D>(MuzzlePath);
		}

		if (muzzle != null)
		{
			return muzzle;
		}

		muzzle = new Node3D
		{
			Name = "Muzzle",
			Position = FallbackMuzzleLocalPosition
		};

		AddChild(muzzle);
		return muzzle;
	}

	private Vector3 GetTargetAimPosition()
	{
		return _target.GlobalPosition + Vector3.Up * TargetAimHeight;
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
			$"shoot_cooldown={_shootCooldownTimer:0.00}"
		);
	}

	private void LogDebug(string message, bool force = true)
	{
		if (!DebugLogsEnabled && !force)
		{
			return;
		}

		GD.Print($"[Pistol:{Name}] {message}");
	}

	private void LogError(string message)
	{
		GD.PushError($"[Pistol:{Name}] {message}");
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

	private static void OrientNode(Node3D node, Vector3 direction)
	{
		if (direction.LengthSquared() < 0.0001f)
		{
			return;
		}

		Vector3 normalizedDirection = direction.Normalized();
		Vector3 up = Mathf.Abs(normalizedDirection.Dot(Vector3.Up)) > 0.98f ? new Vector3(0.0f, 0.0f, -1.0f) : Vector3.Up;
		node.LookAt(node.GlobalPosition + normalizedDirection, up);
	}
}
