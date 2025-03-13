# Proyecto de Sistema de Reservas Restaurante

## Descripción

Este proyecto implementa un sistema de reservas para restaurantes, compuesto por tres componentes principales:

1. **Clientes**: Aplicaciones de consola que se conectan al servidor intermedio mediante sockets TCP.
2. **Servidor Intermedio**: Un servidor socket que gestiona las conexiones de los clientes y se comunica con la API REST.
3. **API REST**: Un servicio web que implementa operaciones CRUD para las reservas.

## Requisitos Cumplidos

### RA3 - Programación de comunicaciones en red
- Comunicación cliente-servidor mediante sockets TCP
- Conexiones asíncronas de múltiples clientes
- Bloqueo de recursos compartidos con semáforos
- Serialización de mensajes en formato JSON

### RA4 - Generación de servicios en red
- API REST con operaciones CRUD completas
- Persistencia en archivos JSON
- Replicación en Firebase
- Servidor intermedio que actúa como proxy entre clientes y API

### RA5 - Técnicas criptográficas y programación segura
- Cifrado asimétrico (RSA) entre clientes y servidor intermedio
- Firmas digitales para autenticación
- Registro unidireccional (audit logging) de operaciones CRUD
- Registro de identidad de quien realiza las peticiones

## Estructura del Proyecto

```
PSP_PROYECTO_NUEVO/
├── Clients/
│   └── Client/               # Cliente de consola
│       ├── Models.cs         # Modelos de datos
│       ├── CryptoService.cs  # Servicio de cifrado
│       ├── SocketClient.cs   # Cliente socket
│       ├── Program.cs        # Programa principal
│       └── Client.csproj     # Archivo de proyecto
│
├── IntermediateServer/       # Servidor intermedio
│   ├── Models.cs             # Modelos compartidos
│   ├── CryptoService.cs      # Servicio de cifrado
│   ├── AuditService.cs       # Servicio de auditoría
│   ├── SocketServer.cs       # Servidor socket
│   ├── ApiClient.cs          # Cliente para la API REST
│   ├── Program.cs            # Programa principal
│   └── IntermediateServer.csproj  # Archivo de proyecto
│
└── API/                      # API REST
    ├── Controllers/          # Controladores REST
    ├── Models/               # Modelos de datos
    ├── Services/             # Servicios
    ├── Program.cs            # Configuración de la API
    └── API.csproj            # Archivo de proyecto
```

## Características Principales

1. **Comunicación Asíncrona**: Permite múltiples conexiones cliente-servidor simultáneas.
2. **Cifrado Asimétrico**: Garantiza la confidencialidad de las comunicaciones.
3. **Firmas Digitales**: Autentican el origen de los mensajes y previenen alteraciones.
4. **Registro de Auditoría**: Registra todas las operaciones realizadas en el sistema.
5. **Persistencia Dual**: Almacenamiento en JSON local y replicación en Firebase.
6. **Operaciones CRUD Completas**: Gestión completa de reservas de restaurante.

## Cómo Ejecutar

1. **Iniciar la API REST**:
   ```
   cd API
   dotnet run
   ```

2. **Iniciar el Servidor Intermedio**:
   ```
   cd IntermediateServer
   dotnet run
   ```

3. **Iniciar un Cliente**:
   ```
   cd Clients/Client
   dotnet run
   ```

## Diagrama de Comunicación

```
+--------+     +-------------------+     +---------+
| Cliente | <-> | Servidor         | <-> | API REST|
| Socket  |     | Intermedio       |     |         |
+--------+     +-------------------+     +---------+
                       ^                      |
                       |                      v
                 +------------+          +----------+
                 | Auditoría  |          | Firebase |
                 | (JSON)     |          | (JSON)   |
                 +------------+          +----------+
```

## Seguridad

- Entre clientes y servidor intermedio: **Cifrado asimétrico RSA**
- Entre servidor intermedio y API: **Registro unidireccional de identidad**
- Integridad de mensajes: **Firmas digitales con SHA-256**

## Pruebas con Postman

La API REST puede probarse con Postman utilizando los siguientes endpoints:

- `GET /api/reservations` - Listar todas las reservas
- `GET /api/reservations/{id}` - Obtener una reserva específica
- `POST /api/reservations` - Crear una nueva reserva
- `PUT /api/reservations/{id}` - Actualizar una reserva existente
- `DELETE /api/reservations/{id}` - Eliminar una reserva

## Autor

Creado para el módulo de Programación de Servicios y Procesos. 