using Godot;

public partial class Enemy : CharacterBody3D
{
	[Export] public NodePath TargetPath { get; set; }
	[Export] public NodePath NavigationAgentPath { get; set; } = "NavigationAgent3D";
	[Export] public NodePath TouchAreaPath { get; set; } = "TouchArea";
	[Export] public float Speed { get; set; } = 4.5f;
	[Export] public float Acceleration { get; set; } = 18.0f;
	[Export] public float Gravity { get; set; } = 24.0f;
	[Export] public float TouchDistance { get; set; } = 0.9f;
	[Export] public float TargetRefreshSeconds { get; set; } = 0.1f;
	[Export] public float MaxHealth { get; set; } = 30.0f;
	[Export] public NodePath HealthBarRootPath { get; set; } = "HealthBarRoot";
	[Export] public NodePath HealthBarFillPath { get; set; } = "HealthBarRoot/Fill";
	[Export] public PackedScene DeathExplosionScene { get; set; } = GD.Load<PackedScene>("res://addons/ExplosionFXFree/assets/BinbunVFX_Vol2/ExplosionFX/effects/ground/vfx_ground_explosion_01.tscn");
	[Export] public float DeathExplosionScale { get; set; } = 0.6f;
	[Export] public bool UseDirectFallback { get; set; } = true;
	[Export] public float MinPathPointDistance { get; set; } = 0.25f;
	[Export] public bool DebugLogsEnabled { get; set; } = true;
	[Export] public float DebugLogIntervalSeconds { get; set; } = 1.0f;

	private const float HealthBarWidth = 1.2f;
	private const float HealthBarHeight = 0.12f;

	private NavigationAgent3D _navigationAgent;
	private Area3D _touchArea;
	private Node3D _healthBarRoot;
	private MeshInstance3D _healthBarFill;
	private Node3D _target;
	private double _targetRefreshTimer;
	private double _debugLogTimer;
	private float _health;
	private bool _isDead;
	private bool _warnedAboutTargetPath;
	private bool _warnedAboutMissingTarget;
	private string _lastMoveState = "not initialized";

	public override void _Ready()
	{
		_health = MaxHealth;
		_navigationAgent = ResolveNavigationAgent();
		_touchArea = GetNodeOrNull<Area3D>(TouchAreaPath);
		_healthBarRoot = GetNodeOrNull<Node3D>(HealthBarRootPath);
		_healthBarFill = GetNodeOrNull<MeshInstance3D>(HealthBarFillPath);

		if (_navigationAgent != null)
		{
			_navigationAgent.PathDesiredDistance = 0.35f;
			_navigationAgent.TargetDesiredDistance = TouchDistance;
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
			LogError($"Touch Area3D not found by path '{TouchAreaPath}'. Enemy can still move, but touch death area will not work.");
		}

		ResolveTarget();
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

		if (_target != null && _navigationAgent != null)
		{
			UpdateTargetPosition(delta);
			ChaseTarget(delta);
		}
		else
		{
			Decelerate(delta);
		}

		MoveAndSlide();
		DieIfTouchingPlayer();
		FaceHealthBarToCamera();
		LogMovementStatus(delta);
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
		LogDebug($"target position updated: {FormatVector(_navigationAgent.TargetPosition)}", false);
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

	private void DieIfTouchingPlayer()
	{
		for (int i = 0; i < GetSlideCollisionCount(); i++)
		{
			KinematicCollision3D collision = GetSlideCollision(i);

			if (collision.GetCollider() is Node collider && IsPlayerNode(collider))
			{
				Die();
				return;
			}
		}

		if (_target != null && GlobalPosition.DistanceTo(_target.GlobalPosition) <= TouchDistance)
		{
			Die();
		}
	}

	private void OnTouchAreaBodyEntered(Node3D body)
	{
		if (IsPlayerNode(body))
		{
			Die();
		}
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
		LogDebug("died by touching player");
		SpawnDeathExplosion();
		QueueFree();
	}

	private void SpawnDeathExplosion()
	{
		if (DeathExplosionScene == null)
		{
			return;
		}

		Node3D explosion = DeathExplosionScene.Instantiate<Node3D>();
		Node parent = GetTree().CurrentScene ?? GetParent();

		if (parent == null)
		{
			explosion.QueueFree();
			return;
		}

		explosion.Name = $"{Name}DeathExplosion";
		explosion.TopLevel = true;
		explosion.Set("one_shot", true);
		explosion.Set("autoplay", false);

		if (explosion.HasSignal("finished"))
		{
			explosion.Connect("finished", Callable.From(() => explosion.QueueFree()));
		}

		parent.AddChild(explosion);
		explosion.GlobalPosition = GlobalPosition;
		explosion.Scale = Vector3.One * DeathExplosionScale;
		DisableExplosionAnimationLoop(explosion);

		if (explosion.HasMethod("play"))
		{
			explosion.Call("play");
		}

		GetTree().CreateTimer(4.0).Timeout += () =>
		{
			if (GodotObject.IsInstanceValid(explosion))
			{
				explosion.QueueFree();
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

		if (_navigationAgent == null)
		{
			LogError("not moving by navigation: NavigationAgent3D is null");
			return;
		}

		Vector3 nextPathPosition = _navigationAgent.GetNextPathPosition();
		float distanceToTarget = GlobalPosition.DistanceTo(_target.GlobalPosition);
		float horizontalSpeed = new Vector2(Velocity.X, Velocity.Z).Length();

		LogDebug(
			$"state={_lastMoveState}; " +
			$"pos={FormatVector(GlobalPosition)}; " +
			$"target={FormatNode(_target)} at {FormatVector(_target.GlobalPosition)}; " +
			$"distance={distanceToTarget:0.00}; " +
			$"velocity={FormatVector(Velocity)}; horizontal_speed={horizontalSpeed:0.00}; " +
			$"nav_finished={_navigationAgent.IsNavigationFinished()}; " +
			$"nav_target={FormatVector(_navigationAgent.TargetPosition)}; " +
			$"next_path={FormatVector(nextPathPosition)}"
		);

		if (horizontalSpeed <= 0.01f && distanceToTarget > TouchDistance)
		{
			LogError(
				"enemy is not moving. Check that NavigationRegion3D has a baked NavigationMesh, " +
				"enemy and player are standing on the navmesh, and collision layers do not block movement."
			);
		}
	}

	private void LogDebug(string message, bool force = true)
	{
		if (!DebugLogsEnabled && !force)
		{
			return;
		}

		GD.Print($"[Enemy:{Name}] {message}");
	}

	private void LogError(string message)
	{
		GD.PushError($"[Enemy:{Name}] {message}");
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
