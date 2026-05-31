using Godot;

namespace Shapeshift.Weapons.Projectiles;

public partial class RoundProjectile : Area3D
{
	[Export] public float Speed { get; set; } = 22.0f;
	[Export] public float ProjectileGravity { get; set; } = 0.0f;
	[Export] public float Damage { get; set; } = 12.0f;
	[Export] public float LifeSeconds { get; set; } = 4.0f;
	[Export] public float MaxDistance { get; set; } = 45.0f;
	[Export(PropertyHint.Layers3DPhysics)] public uint HitMask { get; set; } = 3;
	[Export] public bool DestroyOnHit { get; set; } = true;

	private readonly Godot.Collections.Array<Rid> _excludedRids = new();

	private Vector3 _velocity;
	private Node _source;
	private float _age;
	private float _distanceTravelled;
	private bool _destroyed;

	public override void _Ready()
	{
		CollisionLayer = 0;
		CollisionMask = HitMask;
		Monitoring = true;
		Monitorable = false;
		BodyEntered += OnBodyEntered;
		AreaEntered += OnAreaEntered;

		if (_velocity.LengthSquared() < 0.0001f)
		{
			Launch(-GlobalTransform.Basis.Z.Normalized(), _source);
		}
	}

	public void Configure(float speed, float gravity, float damage, float lifeSeconds, float maxDistance, uint hitMask)
	{
		Speed = speed;
		ProjectileGravity = gravity;
		Damage = damage;
		LifeSeconds = lifeSeconds;
		MaxDistance = maxDistance;
		HitMask = hitMask;
		CollisionMask = HitMask;
	}

	public void Launch(Vector3 direction, Node source)
	{
		_source = source;
		RebuildExclusionRids();

		if (direction.LengthSquared() < 0.0001f)
		{
			direction = -GlobalTransform.Basis.Z;
		}

		_velocity = direction.Normalized() * Mathf.Max(0.0f, Speed);
		OrientToVelocity();
	}

	public override void _PhysicsProcess(double delta)
	{
		if (_destroyed)
		{
			return;
		}

		float step = (float)delta;
		_age += step;

		if (_age >= LifeSeconds)
		{
			Destroy();
			return;
		}

		_velocity += Vector3.Down * ProjectileGravity * step;
		Vector3 startPosition = GlobalPosition;
		Vector3 motion = _velocity * step;

		if (motion.LengthSquared() <= 0.000001f)
		{
			return;
		}

		Vector3 endPosition = startPosition + motion;
		Godot.Collections.Dictionary hit = CastFlightSegment(startPosition, endPosition);

		if (hit.Count > 0)
		{
			GlobalPosition = hit["position"].AsVector3();
			GodotObject colliderObject = hit["collider"].AsGodotObject();
			Hit(colliderObject as Node);
			return;
		}

		GlobalPosition = endPosition;
		_distanceTravelled += motion.Length();
		OrientToVelocity();

		if (_distanceTravelled >= MaxDistance)
		{
			Destroy();
		}
	}

	private Godot.Collections.Dictionary CastFlightSegment(Vector3 from, Vector3 to)
	{
		PhysicsRayQueryParameters3D query = PhysicsRayQueryParameters3D.Create(from, to);
		query.CollisionMask = HitMask;
		query.CollideWithAreas = true;
		query.CollideWithBodies = true;
		query.Exclude = _excludedRids;

		return GetWorld3D().DirectSpaceState.IntersectRay(query);
	}

	private void OnBodyEntered(Node3D body)
	{
		Hit(body);
	}

	private void OnAreaEntered(Area3D area)
	{
		Hit(area);
	}

	private void Hit(Node collider)
	{
		if (_destroyed || collider == null || ShouldIgnore(collider))
		{
			return;
		}

		Node receiver = FindDamageReceiver(collider);

		if (receiver != null)
		{
			ApplyDamage(receiver);
		}

		if (DestroyOnHit)
		{
			Destroy();
		}
	}

	private void ApplyDamage(Node receiver)
	{
		if (receiver.HasMethod("ApplyDamage"))
		{
			receiver.Call("ApplyDamage", Damage, _source ?? this);
		}
		else if (receiver.HasMethod("apply_damage"))
		{
			receiver.Call("apply_damage", Damage, _source ?? this);
		}
		else if (receiver.HasMethod("TakeDamage"))
		{
			receiver.Call("TakeDamage", Damage, _source ?? this);
		}
		else if (receiver.HasMethod("take_damage"))
		{
			receiver.Call("take_damage", Damage, _source ?? this);
		}
	}

	private void Destroy()
	{
		if (_destroyed)
		{
			return;
		}

		_destroyed = true;
		SetPhysicsProcess(false);
		QueueFree();
	}

	private void RebuildExclusionRids()
	{
		_excludedRids.Clear();

		if (this is CollisionObject3D selfCollision)
		{
			_excludedRids.Add(selfCollision.GetRid());
		}

		Node current = _source;

		while (current != null)
		{
			if (current is CollisionObject3D collisionObject)
			{
				_excludedRids.Add(collisionObject.GetRid());
			}

			current = current.GetParent();
		}
	}

	private bool ShouldIgnore(Node collider)
	{
		if (_source == null)
		{
			return false;
		}

		Node current = collider;

		while (current != null)
		{
			if (current == _source)
			{
				return true;
			}

			current = current.GetParent();
		}

		return false;
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

	private void OrientToVelocity()
	{
		if (_velocity.LengthSquared() < 0.0001f)
		{
			return;
		}

		Vector3 direction = _velocity.Normalized();
		Vector3 up = Mathf.Abs(direction.Dot(Vector3.Up)) > 0.98f ? new Vector3(0.0f, 0.0f, -1.0f) : Vector3.Up;
		LookAt(GlobalPosition + direction, up);
	}
}
