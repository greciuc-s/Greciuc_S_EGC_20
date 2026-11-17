using OpenTK.Mathematics;
using OpenTK.Windowing.GraphicsLibraryFramework;

namespace Egc;

/// <summary>
/// NOU: scenă adițională — o mașină care se mișcă înainte/înapoi și în jurul unei sfere.
/// Mașina este construită ierarhic (secțiunea 6): un pivot fără formă poartă poziția și direcția,
/// iar caroseria, cabina, farurile și roțile sunt copiii lui. Două moduri:
///  - DEMO (pornit la încărcare, tasta O îl reactivează): mașina înconjoară sfera, iar distanța
///    față de ea "respiră" cu un Tween PingPong cu EaseInOut — mașina merge înainte pe direcția
///    razei (se apropie de sferă) și înapoi (se îndepărtează), la nesfârșit;
///  - LA TASTATURĂ (W A S D sau săgețile; prima apăsare preia comanda): accelerație, mers înapoi
///    și virare proporțională cu viteza, ca la o mașină adevărată. Sfera este un obstacol circular.
/// Roțile se învârt cu viteza mașinii (ω = v / r), iar sfera se rotește încet (Spin).
/// Toate animațiile folosesc doar timpul de joc (dt), deci P, [ ] și B funcționează și aici.
/// </summary>
public sealed class CarScene : Scene
{
    // Acțiunile proprii scenei (deplasarea reutiliză numele comune din Actions).
    private const string AutoDemo = "demo: în jurul sferei";
    private const string ResetCar = "repoziționează mașina";

    // Geometria arenei.
    private const float SphereRadius = 1.2f;    // sfera din centru
    private const float MinRadius = 4.2f;       // cea mai apropiată distanță în demo
    private const float MaxRadius = 7.2f;       // cea mai îndepărtată distanță în demo
    private const float ArenaLimit = 8.5f;      // limita planșeului

    // "Motorul": accelerație, viteze limită, virare.
    private const float Acceleration = 8.0f;    // unități / s²
    private const float MaxForwardSpeed = 6.0f; // unități / s
    private const float MaxReverseSpeed = 3.0f; // unități / s (mers înapoi)
    private const float Drag = 5.0f;            // frânarea naturală, unități / s²
    private const float TurnRate = 110.0f;      // grade / s, la viteza de referință
    private const float AutoTurn = 45.0f;       // grade / s în jurul sferei (un tur = 8 s)
    private const float WheelRadius = 0.22f;    // pentru unghiul roților: ω = v / r

    private SceneObject _car = null!;           // pivotul mașinii (fără formă)
    private readonly List<SceneObject> _wheels = new();
    private Tween _radius = null!;              // distanța față de sferă, în modul demo

    private bool _auto = true;                  // demo sau la tastatură
    private float _angle;                       // unghiul în jurul sferei (grade)
    private float _heading;                     // direcția mașinii (grade, în jurul lui Y)
    private float _speed;                       // viteza pe direcția mașinii (cu semn)
    private float _wheelSpin;                   // unghiul roților (grade)

    public override string Name => "mașina și sfera";

    public override IEnumerable<string> ActionNames => new[]
    {
        Actions.Forward, Actions.Back, Actions.Left, Actions.Right, AutoDemo, ResetCar,
    };

    public override void Load(Input input)
    {
        CameraPosition = new Vector3(0.0f, 9.0f, 13.0f);
        CameraTarget = new Vector3(0.0f, 1.0f, 0.0f);

        input.Bind(Actions.Forward, Keys.W, Keys.Up);
        input.Bind(Actions.Back, Keys.S, Keys.Down);
        input.Bind(Actions.Left, Keys.A, Keys.Left);
        input.Bind(Actions.Right, Keys.D, Keys.Right);
        input.Bind(AutoDemo, Keys.O);
        input.Bind(ResetCar, Keys.R);

        Mesh grid = AddMesh(Shapes.Grid(10, 1.0f, new Color4(0.30f, 0.33f, 0.38f, 1.0f)));
        Mesh cube = AddMesh(Shapes.ShadedCube());
        Mesh sphere = AddMesh(Shapes.ShadedSphere(SphereRadius, 32, 16));
        Mesh wheel = AddMesh(Shapes.ShadedCylinder(WheelRadius, 0.12f, 16));

        AddObject("podea", grid);

        SceneObject ball = AddObject("sfera", sphere);
        ball.Transform.Position = new Vector3(0.0f, SphereRadius, 0.0f);
        ball.Tint = new Color4(0.35f, 0.55f, 1.0f, 1.0f);
        ball.Spin = new Vector3(0.0f, 15.0f, 0.0f);

        // Un inel de marcaje la raza medie a demonstrației, ca să se vadă drumul mașinii.
        float mid = 0.5f * (MinRadius + MaxRadius);
        for (int i = 0; i < 12; i++)
        {
            SceneObject marker = AddObject($"marcaj {i}", cube);
            float a = MathHelper.DegreesToRadians(i * 30.0f);
            marker.Transform.Position = new Vector3(MathF.Sin(a) * mid, 0.02f, MathF.Cos(a) * mid);
            marker.Transform.Scale = new Vector3(0.12f);
            marker.Tint = new Color4(0.45f, 0.5f, 0.6f, 1.0f);
        }

        BuildCar(cube, wheel);

        // Demo: distanța față de sferă merge înainte/înapoi cu EaseInOut, dus-întors la nesfârșit.
        _radius = new Tween(new Vector3(MinRadius, 0.0f, 0.0f), new Vector3(MaxRadius, 0.0f, 0.0f),
                            2.5f, Easing.EaseInOut, pingPong: true);
        Reset();
    }

    // --- Mașina, construită ierarhic ------------------------------------------------------------

    private void BuildCar(Mesh cube, Mesh wheel)
    {
        _car = AddObject("mașina", null);   // pivotul: poziția și direcția mașinii în lume

        SceneObject body = AddObject("caroserie", cube, _car);
        body.Transform.Position = new Vector3(0.0f, 0.42f, 0.0f);
        body.Transform.Scale = new Vector3(0.95f, 0.32f, 1.7f);
        body.Tint = new Color4(0.85f, 0.25f, 0.2f, 1.0f);

        SceneObject cabin = AddObject("cabină", cube, _car);
        cabin.Transform.Position = new Vector3(0.0f, 0.72f, -0.2f);
        cabin.Transform.Scale = new Vector3(0.7f, 0.28f, 0.75f);
        cabin.Tint = new Color4(0.85f, 0.9f, 0.95f, 1.0f);

        foreach (float x in new[] { -0.28f, 0.28f })
        {
            SceneObject light = AddObject("far", cube, _car);
            light.Transform.Position = new Vector3(x, 0.45f, 0.88f);
            light.Transform.Scale = new Vector3(0.12f);
            light.Tint = new Color4(1.0f, 0.9f, 0.3f, 1.0f);
        }

        // Fiecare roată stă pe propriul pivot, rotit cu -90° în jurul axei Z: axa cilindrului (Y)
        // devine axa roții, pe lățimea mașinii. Roata-copil se rotește apoi în jurul axei ei.
        foreach (float x in new[] { -0.55f, 0.55f })
        {
            foreach (float z in new[] { -0.6f, 0.6f })
            {
                SceneObject pivot = AddObject("pivot roată", null, _car);
                pivot.Transform.Position = new Vector3(x, WheelRadius, z);
                pivot.Transform.Rotation = new Vector3(0.0f, 0.0f, -90.0f);

                SceneObject tire = AddObject("roată", wheel, pivot);
                tire.Tint = new Color4(0.12f, 0.12f, 0.14f, 1.0f);
                _wheels.Add(tire);
            }
        }
    }

    // --- Actualizarea, la fiecare cadru --------------------------------------------------------

    public override void Update(float dt, Input input)
    {
        base.Update(dt, input);   // Spin (sfera)

        // Prima tastă de deplasare preia comanda mașinii.
        bool driving = input.IsDown(Actions.Forward) || input.IsDown(Actions.Back)
                    || input.IsDown(Actions.Left) || input.IsDown(Actions.Right);
        if (_auto && driving)
        {
            _auto = false;
            _speed = 2.0f;   // pornire lină, din mers
            ConsolePanel.PrintMessage("mașina: condusă de la tastatură");
        }

        // O predă din nou demo-ului: aceasta continuă de acolo unde este mașina.
        if (!_auto && input.WasPressed(AutoDemo))
        {
            _auto = true;
            _speed = 0.0f;
            Vector3 p = _car.Transform.Position;
            _angle = MathHelper.RadiansToDegrees(MathF.Atan2(p.X, p.Z));
            float current = MathF.Sqrt(p.X * p.X + p.Z * p.Z);
            float target = current > 0.5f * (MinRadius + MaxRadius) ? MaxRadius : MinRadius;
            // Tween-ul pornește de la distanța curentă (GoTo își ia From din Value).
            _radius.From = _radius.To = new Vector3(MathF.Max(current, MinRadius), 0.0f, 0.0f);
            _radius.GoTo(new Vector3(target, 0.0f, 0.0f), 1.5f);
            ConsolePanel.PrintMessage("mașina: demo, în jurul sferei");
        }

        if (input.WasPressed(ResetCar)) Reset();

        if (_auto)
        {
            UpdateDemo(dt);
        }
        else
        {
            UpdateDriven(dt, input);
        }

        _wheelSpin %= 360.0f;
        foreach (SceneObject tire in _wheels)
        {
            tire.Transform.Rotation = new Vector3(0.0f, _wheelSpin, 0.0f);
        }
    }

    /// <summary>Demo: înconjoară sfera (unghiul crește), iar raza "respiră" prin Tween.</summary>
    private void UpdateDemo(float dt)
    {
        _angle = (_angle + AutoTurn * dt) % 360.0f;
        _radius.Update(dt);

        float r = _radius.Value.X;
        float a = MathHelper.DegreesToRadians(_angle);
        _car.Transform.Position = new Vector3(MathF.Sin(a) * r, 0.0f, MathF.Cos(a) * r);
        _heading = _angle + 90.0f;   // tangenta la cerc: înainte = pe direcția mișcării
        _car.Transform.Rotation = new Vector3(0.0f, _heading, 0.0f);

        float v = MathHelper.DegreesToRadians(AutoTurn) * r;   // viteza tangențială
        _wheelSpin += WheelDegreesPerSecond(v) * dt;
    }

    /// <summary>La tastatură: accelerație / frânare, virare proporțională cu viteza, coliziune cu sfera.</summary>
    private void UpdateDriven(float dt, Input input)
    {
        float throttle = input.Axis(Actions.Back, Actions.Forward);   // +1 = înainte (W), -1 = înapoi (S)
        float steer = input.Axis(Actions.Left, Actions.Right);        // +1 = la dreapta (D)

        if (throttle != 0.0f)
        {
            _speed = Math.Clamp(_speed + throttle * Acceleration * dt, -MaxReverseSpeed, MaxForwardSpeed);
        }
        else   // frânare naturală când nu e apăsat nimic
        {
            float drag = Drag * dt;
            _speed = MathF.Abs(_speed) <= drag ? 0.0f : _speed - MathF.Sign(_speed) * drag;
        }

        // Virarea este proporțională cu viteza și se inversează pe mers înapoi, ca la o mașină reală.
        float grip = Math.Clamp(_speed / 3.0f, -1.0f, 1.0f);
        _heading = (_heading - steer * TurnRate * grip * dt) % 360.0f;

        float a = MathHelper.DegreesToRadians(_heading);
        Vector3 p = _car.Transform.Position;
        p.X = Math.Clamp(p.X + MathF.Sin(a) * _speed * dt, -ArenaLimit, ArenaLimit);
        p.Z = Math.Clamp(p.Z + MathF.Cos(a) * _speed * dt, -ArenaLimit, ArenaLimit);

        // Sfera este un obstacol circular: dacă mașina a intrat în ea, o scoatem pe direcția razei.
        float dist = MathF.Sqrt(p.X * p.X + p.Z * p.Z);
        float minDist = SphereRadius + 0.9f;
        if (dist < minDist && dist > 1e-4f)
        {
            p.X = p.X / dist * minDist;
            p.Z = p.Z / dist * minDist;
        }

        _car.Transform.Position = p;
        _car.Transform.Rotation = new Vector3(0.0f, _heading, 0.0f);
        _wheelSpin += WheelDegreesPerSecond(_speed) * dt;
    }

    /// <summary>Unghiul roților (grade pe secundă) pentru viteza dată: ω = v / r.</summary>
    private static float WheelDegreesPerSecond(float speed) =>
        speed / WheelRadius * MathHelper.RadiansToDegrees(1.0f);

    /// <summary>Mașina revine la pornirea demo-ului, la marginea inelului de marcaje.</summary>
    private void Reset()
    {
        _auto = true;
        _angle = 0.0f;
        _heading = 90.0f;
        _speed = 0.0f;
        _wheelSpin = 0.0f;
        _radius.Restart();
        _car.Transform.Position = new Vector3(0.0f, 0.0f, MinRadius);
        _car.Transform.Rotation = new Vector3(0.0f, _heading, 0.0f);
        ConsolePanel.PrintMessage("mașina a fost repoziționată");
    }

    // --- Starea scenei (titlu și tasta I) -------------------------------------------------------

    public override string Status => _auto
        ? $"mașina: demo, R = {_radius.Value.X:F1}"
        : $"mașina: v = {_speed:F1}";

    public override IEnumerable<string> Describe()
    {
        foreach (string line in base.Describe()) yield return line;
        yield return $"Mod            : {(_auto ? "demo (în jurul sferei)" : "la tastatură")}";
        yield return $"Mașina         : {Fmt(_car.Transform.Position)}   direcția {_heading:F0}°   viteza {_speed:F1} unități/s";
        yield return $"Roți           : {_wheelSpin / 360.0f:F1} ture față de pornire";
        yield return $"Demo           : raza {_radius.Value.X:F1} între {MinRadius} și {MaxRadius}   progres {_radius.Progress:P0}";
    }

    private static string Fmt(Vector3 v) => $"({v.X:F2}, {v.Y:F2}, {v.Z:F2})";
}
