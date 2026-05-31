using System.Collections.Generic;
using Godot;

namespace Shapeshift.Enemies.Boss;

public partial class Enemy : CharacterBody3D
{
	[Export] public NodePath TargetPath { get; set; }
	[Export] public NodePath NavigationAgentPath { get; set; } = "NavigationAgent3D";
	[Export] public NodePath TouchAreaPath { get; set; } = "TouchArea";
	[Export] public float Speed { get; set; } = 2.6f;
	[Export] public float Acceleration { get; set; } = 18.0f;
	[Export] public float Gravity { get; set; } = 24.0f;
	[Export] public float PreferredAttackDistance { get; set; } = 6.0f;
	[Export] public float TargetAimHeight { get; set; } = 0.85f;
	[Export] public float TargetRefreshSeconds { get; set; } = 0.1f;
	[Export] public float MaxHealth { get; set; } = 150.0f;
	[Export] public NodePath HealthBarRootPath { get; set; } = "HealthBarRoot";
	[Export] public NodePath HealthBarFillPath { get; set; } = "HealthBarRoot/Fill";
	[Export] public PackedScene DeathExplosionScene { get; set; } = GD.Load<PackedScene>("res://addons/ExplosionFXFree/assets/BinbunVFX_Vol2/ExplosionFX/effects/ground/vfx_ground_explosion_01.tscn");
	[Export] public float DeathExplosionScale { get; set; } = 1.2f;
	[Export] public AudioStream DeathSound { get; set; } = GD.Load<AudioStream>("res://objects/characters/enemies/vacuum_cleaner/explode.mp3");
	[Export] public float DeathSoundVolumeDb { get; set; } = 1.5f;
	[Export] public float DeathSoundMaxDistance { get; set; } = 36.0f;
	[Export] public bool UseDirectFallback { get; set; } = true;
	[Export] public float MinPathPointDistance { get; set; } = 0.25f;
	[Export] public bool DebugLogsEnabled { get; set; } = false;
	[Export] public float DebugLogIntervalSeconds { get; set; } = 1.0f;

	[ExportGroup("Laser")]
	[Export] public PackedScene LaserWeaponScene { get; set; } = GD.Load<PackedScene>("res://objects/weapons/laser_weapon/laser_weapon.tscn");
	[Export] public NodePath LaserOriginPath { get; set; } = "ProjectileOrigin";
	[Export] public Vector3 FallbackLaserOriginLocalPosition { get; set; } = new(0.0f, 0.65f, -1.2f);
	[Export] public float LaserRange { get; set; } = 16.0f;
	[Export] public float LaserDamagePerSecond { get; set; } = 12.0f;
	[Export] public bool RequireLaserLineOfSight { get; set; } = true;
	[Export(PropertyHint.Layers3DPhysics)] public uint LaserCollisionMask { get; set; } = 3;

	[ExportGroup("Flamethrower")]
	[Export] public PackedScene FlameScene { get; set; } = GD.Load<PackedScene>("res://addons/ExplosionFXFree/assets/FlameFXFree/assets/BinbunVFX_Vol2/FlameFX/effects/fire/vfx_basic_fire_01.tscn");
	[Export] public NodePath FlameOriginPath { get; set; } = "FlameOrigin";
	[Export] public Vector3 FallbackFlameOriginLocalPosition { get; set; } = new(0.0f, 0.95f, -0.4f);
	[Export] public float FlameRange { get; set; } = 6.4f;
	[Export] public float FlameConeDegrees { get; set; } = 38.0f;
	[Export] public float FlameDamagePerSecond { get; set; } = 18.0f;
	[Export(PropertyHint.Layers3DPhysics)] public uint FlameAttackCollisionMask { get; set; } = 3;
	[Export] public bool RequireFlameLineOfSight { get; set; } = true;
	[Export] public int FlameSegmentCount { get; set; } = 6;
	[Export] public int FlameParticlesPerSegment { get; set; } = 44;
	[Export] public float FlameStartScale { get; set; } = 0.6f;
	[Export] public float FlameEndScale { get; set; } = 1.45f;

	[ExportGroup("Electric Attack")]
	[Export] public PackedScene ZapScene { get; set; } = GD.Load<PackedScene>("res://addons/BinbunVFX/ElectricFXFree/assets/BinbunVFX_Vol2/ElectricFX/effects/zap/vfx_zap_lightning_01.tscn");
	[Export] public NodePath LightningOriginPath { get; set; } = "LightningOrigin";
	[Export] public Vector3 FallbackLightningOriginLocalPosition { get; set; } = new(0.0f, 0.95f, -0.25f);
	[Export] public float ShockRange { get; set; } = 8.5f;
	[Export] public float ShockDamage { get; set; } = 20.0f;
	[Export] public float ShockCooldownSeconds { get; set; } = 1.05f;
	[Export] public float FirstShockDelaySeconds { get; set; } = 0.2f;
	[Export] public float MinimumZapLength { get; set; } = 0.35f;
	[Export] public float ZapSpeedScale { get; set; } = 1.35f;
	[Export] public double ZapCleanupSeconds { get; set; } = 1.4;
	[Export] public Color ZapPrimaryColor { get; set; } = new(1.0f, 1.0f, 1.0f, 1.0f);
	[Export] public Color ZapSecondaryColor { get; set; } = new(0.0f, 0.84f, 1.0f, 1.0f);
	[Export] public float ZapEmission { get; set; } = 4.5f;
	[Export] public bool RequireShockLineOfSight { get; set; } = true;
	[Export(PropertyHint.Layers3DPhysics)] public uint ShockAttackCollisionMask { get; set; } = 3;

	private const float HealthBarWidth = 1.2f;
	private const float HealthBarHeight = 0.12f;

	private readonly List<Node3D> _flameSegments = new();

	private NavigationAgent3D _navigationAgent;
	private Area3D _touchArea;
	private Node3D _healthBarRoot;
	private MeshInstance3D _healthBarFill;
	private Node3D _laserOrigin;
	private Node3D _laserWeapon;
	private Node3D _flameOrigin;
	private Node3D _lightningOrigin;
	private Node3D _target;
	private double _targetRefreshTimer;
	private double _debugLogTimer;
	private double _shockCooldownTimer;
	private float _health;
	private bool _isDead;
	private bool _flameActive;
	private bool _laserActive;
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
		_laserOrigin = ResolveOrigin(LaserOriginPath, "ProjectileOrigin", "Projectile Origin", FallbackLaserOriginLocalPosition);
		_flameOrigin = ResolveOrigin(FlameOriginPath, "FlameOrigin", "Flame Origin", FallbackFlameOriginLocalPosition);
		_lightningOrigin = ResolveOrigin(LightningOriginPath, "LightningOrigin", "Lightning Origin", FallbackLightningOriginLocalPosition);

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

		ResolveTarget();
		SetupLaserWeapon();
		SetupFlameSegments();
		UpdateHealthBar();
		LogDebug(
			$"ready. target={FormatNode(_target)}, " +
			$"laser_origin={FormatNode(_laserOrigin)}, laser_weapon={FormatNode(_laserWeapon)}, " +
			$"flame_origin={FormatNode(_flameOrigin)}, " +
			$"lightning_origin={FormatNode(_lightningOrigin)}, " +
			$"nav_agent={FormatNode(_navigationAgent)}"
		);
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

		bool canFlame = false;
		bool canShock = false;
		bool canLaser = false;

		if (_target != null)
		{
			FaceTarget();

			canFlame = CanFlameTarget(out Node flameCollider);
			canShock = CanShockTarget(out Node shockCollider, out Vector3 shockImpactPosition);
			canLaser = CanLaserTarget();
			UpdateLaserAim();

			if (canFlame)
			{
				ApplyFlameDamage(flameCollider, delta);
			}

			if (canShock)
			{
				TryShock(shockCollider, shockImpactPosition);
			}

			if (ShouldHoldAttackPosition(canFlame, canShock, canLaser))
			{
				Decelerate(delta);
				_lastMoveState = BuildAttackState(canFlame, canShock, canLaser);
			}
			else if (_navigationAgent != null)
			{
				UpdateTargetPosition(delta);
				ChaseTarget(delta);
				if (canShock || canLaser)
				{
					_lastMoveState += " while attacking";
				}
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

		SetFlameActive(canFlame);
		SetLaserActive(canLaser);
		MoveAndSlide();
		ShockCollidingPlayer();
		FaceHealthBarToCamera();
		LogMovementStatus(delta);
	}

	private bool ShouldHoldAttackPosition(bool canFlame, bool canShock, bool canLaser)
	{
		if (!canFlame && !canShock && !canLaser)
		{
			return false;
		}

		if (canFlame)
		{
			return true;
		}

		if (_target == null)
		{
			return false;
		}

		return GlobalPosition.DistanceTo(_target.GlobalPosition) <= PreferredAttackDistance;
	}

	private string BuildAttackState(bool canFlame, bool canShock, bool canLaser)
	{
		List<string> attacks = new();

		if (canFlame)
		{
			attacks.Add("flame");
		}

		if (canShock)
		{
			attacks.Add("electricity");
		}

		if (canLaser)
		{
			attacks.Add("laser");
		}

		return attacks.Count == 0 ? "holding attack range" : $"attacking with {string.Join(", ", attacks)}";
	}

	private bool CanFlameTarget(out Node damageCollider)
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

		if (distance > FlameRange || distance < 0.05f)
		{
			return false;
		}

		Vector3 directionToTarget = toTarget / distance;
		Vector3 forward = -_flameOrigin.GlobalTransform.Basis.Z.Normalized();
		float coneCos = Mathf.Cos(Mathf.DegToRad(FlameConeDegrees * 0.5f));

		if (forward.Dot(directionToTarget) < coneCos)
		{
			return false;
		}

		if (!RequireFlameLineOfSight)
		{
			damageCollider = _target;
			return true;
		}

		Godot.Collections.Dictionary result = RaycastAttack(origin, aimPosition, FlameAttackCollisionMask);

		if (result.Count == 0)
		{
			damageCollider = _target;
			return true;
		}

		GodotObject colliderObject = result["collider"].AsGodotObject();
		damageCollider = colliderObject as Node;

		return IsPlayerNode(damageCollider);
	}

	private void ApplyFlameDamage(Node damageCollider, double delta)
	{
		Node receiver = FindDamageReceiver(damageCollider) ?? FindDamageReceiver(_target);

		if (receiver == null)
		{
			return;
		}

		ApplyDamageToReceiver(receiver, FlameDamagePerSecond * (float)delta);
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
			float localDistance = Mathf.Lerp(0.15f, FlameRange * 0.82f, progress);
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
		flame.Set("audio_max_distance", FlameRange + 2.0f);
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

		if (distance > ShockRange || distance < 0.05f)
		{
			return false;
		}

		if (!RequireShockLineOfSight)
		{
			damageCollider = _target;
			impactPosition = aimPosition;
			return true;
		}

		Godot.Collections.Dictionary result = RaycastAttack(origin, aimPosition, ShockAttackCollisionMask);

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
			ApplyDamageToReceiver(receiver, ShockDamage);
		}

		SpawnElectricZap(impactPosition);
		_shockCooldownTimer = ShockCooldownSeconds;
		LogDebug($"shock fired at {FormatVector(impactPosition)}", false);
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
				TryShock(collider, collision.GetPosition());
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

	private void SetupLaserWeapon()
	{
		if (LaserWeaponScene == null || _laserOrigin == null)
		{
			return;
		}

		_laserWeapon = LaserWeaponScene.Instantiate<Node3D>();
		_laserWeapon.Name = "BossLaserWeapon";
		_laserWeapon.Set("use_input", false);
		_laserWeapon.Set("aim_from_camera", false);
		_laserWeapon.Set("use_external_aim", true);
		_laserWeapon.Set("damage_per_second", LaserDamagePerSecond);
		_laserWeapon.Set("max_range", LaserRange);
		_laserWeapon.Set("collision_mask", (int)LaserCollisionMask);

		if (_laserWeapon.HasMethod("set_external_exclusion_root"))
		{
			_laserWeapon.Call("set_external_exclusion_root", this);
		}

		_laserOrigin.AddChild(_laserWeapon);
		_laserWeapon.Position = Vector3.Zero;
		SetLaserActive(false, true);
	}

	private bool CanLaserTarget()
	{
		if (_target == null || _laserOrigin == null || _laserWeapon == null)
		{
			return false;
		}

		Vector3 origin = _laserOrigin.GlobalPosition;
		Vector3 aimPosition = GetTargetAimPosition();

		if (origin.DistanceTo(aimPosition) > LaserRange)
		{
			return false;
		}

		if (!RequireLaserLineOfSight)
		{
			return true;
		}

		Godot.Collections.Dictionary result = RaycastAttack(origin, aimPosition, LaserCollisionMask);

		if (result.Count == 0)
		{
			return true;
		}

		GodotObject colliderObject = result["collider"].AsGodotObject();
		return IsPlayerNode(colliderObject as Node);
	}

	private void UpdateLaserAim()
	{
		if (_laserWeapon == null || _laserOrigin == null || _target == null)
		{
			return;
		}

		Vector3 direction = GetTargetAimPosition() - _laserOrigin.GlobalPosition;

		if (direction.LengthSquared() < 0.0001f)
		{
			direction = -_laserOrigin.GlobalTransform.Basis.Z;
		}

		_laserWeapon.GlobalPosition = _laserOrigin.GlobalPosition;

		if (_laserWeapon.HasMethod("set_external_aim_direction"))
		{
			_laserWeapon.Call("set_external_aim_direction", direction.Normalized());
		}
	}

	private void SetLaserActive(bool active, bool force = false)
	{
		if (!force && _laserActive == active)
		{
			return;
		}

		_laserActive = active;

		if (_laserWeapon != null && GodotObject.IsInstanceValid(_laserWeapon) && _laserWeapon.HasMethod("set_external_firing"))
		{
			_laserWeapon.Call("set_external_firing", active);
		}
	}

	private Vector3 GetTargetAimPosition()
	{
		return _target.GlobalPosition + Vector3.Up * TargetAimHeight;
	}

	private Godot.Collections.Dictionary RaycastAttack(Vector3 origin, Vector3 aimPosition, uint collisionMask)
	{
		PhysicsRayQueryParameters3D query = PhysicsRayQueryParameters3D.Create(origin, aimPosition);
		query.CollisionMask = collisionMask;
		query.CollideWithAreas = true;
		query.CollideWithBodies = true;
		query.Exclude = BuildExclusionRids();

		return GetWorld3D().DirectSpaceState.IntersectRay(query);
	}

	private void ApplyDamageToReceiver(Node receiver, float amount)
	{
		if (receiver == null || amount <= 0.0f)
		{
			return;
		}

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

	private Node3D ResolveOrigin(NodePath configuredPath, string nodeName, string alternateNodeName, Vector3 fallbackPosition)
	{
		Node3D origin = null;

		if (configuredPath != null && !configuredPath.IsEmpty)
		{
			origin = GetNodeOrNull<Node3D>(configuredPath);
		}

		origin ??= FindChild(nodeName, true, false) as Node3D;
		origin ??= FindChild(alternateNodeName, true, false) as Node3D;

		if (origin != null)
		{
			return origin;
		}

		origin = new Node3D
		{
			Name = nodeName,
			Position = fallbackPosition
		};

		AddChild(origin);
		return origin;
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
		SetFlameActive(false, true);
		SetLaserActive(false, true);
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
			$"flame_active={_flameActive}; " +
			$"laser_active={_laserActive}; " +
			$"shock_cooldown={_shockCooldownTimer:0.00}"
		);
	}

	private void LogDebug(string message, bool force = true)
	{
		if (!DebugLogsEnabled && !force)
		{
			return;
		}

		GD.Print($"[Boss:{Name}] {message}");
	}

	private void LogError(string message)
	{
		GD.PushError($"[Boss:{Name}] {message}");
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
