using System.Text.Json;
using Windows.Devices.Bluetooth;
using Windows.Devices.Bluetooth.GenericAttributeProfile;
using Windows.Devices.Enumeration;
using Windows.Storage.Streams;

namespace Escritorio;

/// <summary>
/// Lee la batería de los aparatos Bluetooth conectados (audífonos, mouse, teclado, control…)
/// y la del propio equipo, y se la manda al widget «baterias» cada minuto.
///
/// Dos fuentes, en este orden:
///   1. El nivel que Windows publica en el nodo de cada aparato (el mismo que muestra
///      Configuración → Bluetooth y dispositivos). Sirve para la mayoría, incluidos los clásicos.
///   2. Para aparatos Bluetooth LE sin ese dato, el servicio estándar de batería (GATT 0x180F).
/// </summary>
internal sealed class Baterias : IDisposable
{
    const string ClaveNivel = "{104EA319-6EE2-4701-BD47-8DDBF425BBE5} 2";
    const string ClaveDireccion = "System.Devices.Aep.DeviceAddress";
    const string ClaveConectado = "System.Devices.Aep.IsConnected";
    const string ClaveContenedorAep = "System.Devices.Aep.ContainerId";
    const string ClaveContenedor = "System.Devices.ContainerId";
    const string ClaveInstancia = "System.Devices.DeviceInstanceId";
    // Los nodos de Bluetooth clásico empiezan por BTHENUM y los de LE por BTHLE / BTHLEDEVICE.
    const string FiltroNodos = "System.Devices.DeviceInstanceId:~<\"BTHENUM\" OR System.Devices.DeviceInstanceId:~<\"BTHLE\"";
    static readonly TimeSpan Intervalo = TimeSpan.FromMinutes(1);
    static readonly TimeSpan LimiteGatt = TimeSpan.FromSeconds(5);

    sealed class Aparato(string id, string nombre, string direccion, Guid? contenedor, bool le)
    {
        public string Id { get; } = id;
        public string Nombre { get; } = nombre;
        public string Direccion { get; } = direccion;
        public Guid? Contenedor { get; } = contenedor;
        public bool Le { get; } = le;
        public int? Nivel { get; set; }
    }

    sealed record NivelPublicado(int Nivel, Guid? Contenedor, string Instancia);

    readonly Aplicacion app;
    readonly HashSet<string> avisados = new();
    System.Threading.Timer? reloj;
    int leyendo;
    string? ultimo;

    public Baterias(Aplicacion app) => this.app = app;

    /// <summary>Solo se leen mientras haya un widget de baterías abierto.</summary>
    public void Activar(bool activo)
    {
        if (activo && reloj is null)
            reloj = new System.Threading.Timer(_ => _ = Actualizar(), null, TimeSpan.Zero, Intervalo);
        else if (!activo)
            Dispose();
    }

    public void Dispose()
    {
        reloj?.Dispose();
        reloj = null;
    }

    /// <summary>El widget pidió datos (al abrirse o al hacer clic): se le da lo último y se actualiza.</summary>
    public void Pedir(VentanaWidget widget)
    {
        if (ultimo is not null)
            widget.Enviar(ultimo);
        _ = Actualizar();
    }

    async Task Actualizar()
    {
        if (Interlocked.Exchange(ref leyendo, 1) == 1)
            return;
        try
        {
            var aparatos = await LeerBluetooth();
            var json = JsonSerializer.Serialize(new
            {
                tipo = "baterias",
                equipo = LeerEquipo(),
                dispositivos = aparatos.Select(a => new { nombre = a.Nombre, nivel = a.Nivel }),
            });
            ultimo = json;
            app.EnUI(() => app.EnviarA("baterias", json));
        }
        catch (Exception error)
        {
            Registro.Error("No se pudieron leer las baterías", error);
        }
        finally
        {
            Volatile.Write(ref leyendo, 0);
        }
    }

    static object? LeerEquipo()
    {
        var estado = SystemInformation.PowerStatus;
        if (estado.BatteryChargeStatus.HasFlag(BatteryChargeStatus.NoSystemBattery)
            || estado.BatteryChargeStatus == BatteryChargeStatus.Unknown
            || estado.BatteryLifePercent is < 0 or > 1)
            return null;
        return new
        {
            nombre = "Este equipo",
            nivel = (int)Math.Round(estado.BatteryLifePercent * 100),
            cargando = estado.PowerLineStatus == PowerLineStatus.Online,
        };
    }

    async Task<List<Aparato>> LeerBluetooth()
    {
        // Aparatos emparejados que están conectados ahora, clásicos y LE.
        var aparatos = new Dictionary<string, Aparato>();
        string[] propiedades = [ClaveDireccion, ClaveConectado, ClaveContenedorAep];
        var selectores = new[]
        {
            (BluetoothDevice.GetDeviceSelectorFromPairingState(true), false),
            (BluetoothLEDevice.GetDeviceSelectorFromPairingState(true), true),
        };
        foreach (var (selector, le) in selectores)
        {
            foreach (var info in await DeviceInformation.FindAllAsync(selector, propiedades, DeviceInformationKind.AssociationEndpoint))
            {
                if (!(info.Properties.TryGetValue(ClaveConectado, out var conectado) && conectado is true))
                    continue;
                var direccion = Direccion(info.Properties.TryGetValue(ClaveDireccion, out var valor) ? valor as string : null);
                if (direccion.Length != 12)
                    continue;
                Guid? contenedor = info.Properties.TryGetValue(ClaveContenedorAep, out var g) && g is Guid guid ? guid : null;
                // Un mismo aparato puede aparecer como clásico y como LE: se cuenta una vez.
                aparatos.TryAdd(contenedor?.ToString() ?? direccion, new Aparato(info.Id, info.Name, direccion, contenedor, le));
            }
        }
        if (aparatos.Count == 0)
            return [];

        var publicados = await NivelesPublicados();
        foreach (var aparato in aparatos.Values)
        {
            aparato.Nivel = publicados.FirstOrDefault(n =>
                (aparato.Contenedor is { } g && n.Contenedor == g)
                || n.Instancia.Contains(aparato.Direccion, StringComparison.OrdinalIgnoreCase))?.Nivel;
        }
        foreach (var aparato in aparatos.Values.Where(a => a.Nivel is null && a.Le))
            aparato.Nivel = await LeerGatt(aparato);

        return aparatos.Values.OrderBy(a => a.Nombre, StringComparer.CurrentCultureIgnoreCase).ToList();
    }

    /// <summary>"a0:b1:c2:d3:e4:f5" → "A0B1C2D3E4F5", como aparece en los identificadores de Windows.</summary>
    static string Direccion(string? texto) =>
        new string((texto ?? "").Where(Uri.IsHexDigit).ToArray()).ToUpperInvariant();

    async Task<List<NivelPublicado>> NivelesPublicados()
    {
        var niveles = await BuscarNiveles(FiltroNodos);
        // Si el filtro no encontró nada (o Windows no lo aceptó), se revisan todos los dispositivos.
        if (niveles.Count == 0)
            niveles = await BuscarNiveles("");
        return niveles;
    }

    async Task<List<NivelPublicado>> BuscarNiveles(string filtro)
    {
        var niveles = new List<NivelPublicado>();
        try
        {
            var nodos = await DeviceInformation.FindAllAsync(filtro, [ClaveNivel, ClaveContenedor, ClaveInstancia], DeviceInformationKind.Device);
            foreach (var nodo in nodos)
            {
                if (!nodo.Properties.TryGetValue(ClaveNivel, out var nivel) || nivel is not byte porcentaje)
                    continue;
                Guid? contenedor = nodo.Properties.TryGetValue(ClaveContenedor, out var g) && g is Guid guid ? guid : null;
                var instancia = nodo.Properties.TryGetValue(ClaveInstancia, out var i) && i is string texto ? texto : nodo.Id;
                niveles.Add(new NivelPublicado(porcentaje, contenedor, instancia));
            }
        }
        catch (Exception error)
        {
            if (avisados.Add("filtro:" + filtro))
                Registro.Error($"No se pudo buscar el nivel de batería (filtro «{filtro}»)", error);
        }
        return niveles;
    }

    async Task<int?> LeerGatt(Aparato aparato)
    {
        try
        {
            using var dispositivo = await BluetoothLEDevice.FromIdAsync(aparato.Id).AsTask().WaitAsync(LimiteGatt);
            if (dispositivo is null || dispositivo.ConnectionStatus != BluetoothConnectionStatus.Connected)
                return null;
            var servicios = await dispositivo.GetGattServicesForUuidAsync(GattServiceUuids.Battery, BluetoothCacheMode.Uncached)
                .AsTask().WaitAsync(LimiteGatt);
            if (servicios.Status != GattCommunicationStatus.Success)
                return null;
            int? nivel = null;
            foreach (var servicio in servicios.Services)
            {
                using (servicio)
                {
                    if (nivel is not null)
                        continue;
                    var caracteristicas = await servicio.GetCharacteristicsForUuidAsync(GattCharacteristicUuids.BatteryLevel, BluetoothCacheMode.Uncached)
                        .AsTask().WaitAsync(LimiteGatt);
                    if (caracteristicas.Status != GattCommunicationStatus.Success || caracteristicas.Characteristics.Count == 0)
                        continue;
                    var lectura = await caracteristicas.Characteristics[0].ReadValueAsync(BluetoothCacheMode.Uncached)
                        .AsTask().WaitAsync(LimiteGatt);
                    if (lectura.Status == GattCommunicationStatus.Success && lectura.Value.Length >= 1)
                        nivel = DataReader.FromBuffer(lectura.Value).ReadByte();
                }
            }
            return nivel;
        }
        catch (Exception error)
        {
            // Algunos aparatos (p. ej. teclados y mouse HID) no dejan leer su servicio de batería: se anota una vez.
            if (avisados.Add("gatt:" + aparato.Direccion))
                Registro.Info($"Sin lectura GATT de batería para «{aparato.Nombre}»: {error.GetType().Name}: {error.Message}");
            return null;
        }
    }
}
