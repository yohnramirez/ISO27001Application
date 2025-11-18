
using AppISO.Context;
using AppISO.DataTranferObjects;
using AppISO.Entities;
using AppISO.Helpers;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using System.Security.Claims;
using System.Text;

namespace AppISO
{
    public class Program
    {
        public static void Main(string[] args)
        {
            var builder = WebApplication.CreateBuilder(args);

            // Crea el contexto con la base de datos en memoria
            builder.Services.AddDbContext<AppISOContext>(options => options.UseInMemoryDatabase("AppISODemo"));

            // ===============================================
            // A.9.1.1 – Política de control de acceso
            // ===============================================

            // Configura la autenticacion por medio de politicas
            builder.Services.AddAuthorization(options =>
            {
                // Unicamente RRHH o Gerencia puede ver los salarios
                options.AddPolicy("CanViewSalaries", policy => policy.RequireRole("HR", "Manager"));

                // Unicamente AdminSecurity puede crear/desactivar usuarios
                options.AddPolicy("CanManageUsers", policy => policy.RequireRole("AdminSecurity"));
            });

            // Configuracion del JWT
            var jwtKey = builder.Configuration["Jwt:Key"] ?? "kEY-2025@*ComP4Ny-Is0-SecretKey!";
            var signinKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtKey));

            // Configura JWT para la autenticacion
            builder.Services
                .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
                .AddJwtBearer(options =>
                {
                    options.TokenValidationParameters = new TokenValidationParameters
                    {
                        ValidateIssuer = false,
                        ValidateAudience = false,
                        ValidateIssuerSigningKey = true,
                        IssuerSigningKey = signinKey,
                        ClockSkew = TimeSpan.Zero
                    };
                });


            // Learn more about configuring Swagger/OpenAPI at https://aka.ms/aspnetcore/swashbuckle
            builder.Services.AddEndpointsApiExplorer();
            builder.Services.AddSwaggerGen();

            var app = builder.Build();

            app.UseAuthentication();
            app.UseAuthorization();

            #region Creacion de datos semillas para pruebas
            using (var scope = app.Services.CreateScope())
            {
                var context = scope.ServiceProvider.GetRequiredService<AppISOContext>();

                // Valida si no existen usuarios
                if (!context.Users.Any())
                {
                    // Creacion de usuario administrador
                    context.Users.Add(new User
                    {
                        UserName = "security.admin",
                        PasswordHash = BCrypt.Net.BCrypt.HashPassword("Admin123"),
                        Role = "AdminSecurity",
                        IsActive = true,
                    });

                    // Creacion de usuario gerente
                    context.Users.Add(new User
                    {
                        UserName = "manager.general",
                        PasswordHash = BCrypt.Net.BCrypt.HashPassword("Manager123"),
                        Role = "Manager",
                        IsActive = true,
                    });

                    // Creacion de empleado
                    context.Employees.Add(new Employee
                    {
                        Id = 1,
                        Name = "Pepito Perez",
                        Salary = 3500000m
                    });

                    // Guarda los cambios
                    context.SaveChanges();
                }
            }
            #endregion

            // ===============================================
            // A.9.2.1 – Registro y baja de usuarios
            // ===============================================

            // Creacion de usuarios. Esto requiere rol AdminSecurity
            app.MapPost("/users", async (CreateUserRequestDto request, AppISOContext context) =>
            {
                // Valida si el usuario ya existe
                if (await context.Users.AnyAsync(x => x.UserName == request.UserName))
                    return Results.BadRequest("El usuario ya existe.");

                // Crea la entidad
                var user = new User
                {
                    UserName = request.UserName,
                    PasswordHash = BCrypt.Net.BCrypt.HashPassword(request.Password),
                    Role = request.Role,
                    IsActive = true,
                };

                // Agrega el usuario al contexto
                context.Users.Add(user);

                // Guarda los cambios
                await context.SaveChangesAsync();

                // Retorna los datos de creacion
                return Results.Created($"/users/{user.Id}", new
                {
                    user.Id,
                    user.UserName,
                    user.Role
                });
            }).RequireAuthorization("CanManageUsers");

            // Desactivacion de usuarios. Esto requiere rol AdminSecurity
            app.MapPatch("/users/{id:int}/deactivate", async (int id, AppISOContext context) =>
            {
                // Busca el usuario
                var user = await context.Users.FindAsync(id);

                // Valida si existe
                if (user is null)
                    return Results.NotFound("Usuario no encontrado.");

                // Modifica el estado a inactivo
                user.IsActive = false;

                // Guarda los cambios
                await context.SaveChangesAsync();

                // Retorna el mensaje
                return Results.Ok(new
                {
                    Message = "Usuario desactivado exitosamente!"
                });
            }).RequireAuthorization("CanManageUsers");

            // ===============================================
            // A.9.4.2 – Procedimientos seguros de inicio de sesión
            // ===============================================

            // Logeo de usuarios
            app.MapPost("/login", async (LoginRequestDto request, AppISOContext context) =>
            {
                // Busca el usuario
                var user = await context.Users.SingleOrDefaultAsync(x => x.UserName == request.UserName);

                // Valida la existencia y si esta activo
                if (user is null || !user.IsActive)
                    return Results.Unauthorized();

                // Valida si tiene un bloqueo por reintentos
                if (user.LockTimeEnd.HasValue && user.LockTimeEnd.Value > DateTime.UtcNow)
                    return Results.Unauthorized();

                // Valida si la contrasenia coincide con la guardada
                var validatePassword = BCrypt.Net.BCrypt.Verify(request.Password, user.PasswordHash);

                // Si no coincide
                if (!validatePassword)
                {
                    // Aumenta el contador de fallos en el logueo
                    user.FailedLoginAttemps++;

                    // Valida si supera los 3 intentos
                    if (user.FailedLoginAttemps >= 3)
                    {
                        // Asigna un bloqueo de 15 minutos
                        user.LockTimeEnd = DateTime.UtcNow.AddMinutes(15);
                        user.FailedLoginAttemps = 0;
                    }

                    // Guarda los cambios
                    await context.SaveChangesAsync();

                    // Retorna no autorizado
                    return Results.Unauthorized();
                }

                // Resetea el contador si el login es correcto
                user.FailedLoginAttemps = 0;
                user.LockTimeEnd = null;

                // Guarda los cambios
                await context.SaveChangesAsync();

                // Genera el JWT
                var token = JwtGenerator.Generate(user, signinKey);

                // Retorna el token exitoso
                return Results.Ok(new
                {
                    token
                });
            });

            // ===============================================
            // A.9.1.1 + A.9.2.3 – Política y privilegios de acceso
            // Endpoint protegido para ver salarios
            // ===============================================

            // Obtiene los datos del empleado. Esto requiere rol HR y Manager
            app.MapGet("/employees/{id:int}/salary", async (int id, AppISOContext context) =>
            {
                // Busca el empleado
                var employee = await context.Employees.FindAsync(id);

                // Valida si existe
                if (employee is null)
                    return Results.NotFound("Empleado no encontrado.");

                // Retorna los datos
                return Results.Ok(new
                {
                    employee.Id,
                    employee.Name,
                    employee.Salary
                });
            }).RequireAuthorization("CanViewSalaries");

            // Obtiene los datos del usuario actual. Requiere estar logueado
            app.MapGet("/me", (ClaimsPrincipal user) =>
            {
                // Obtiene el nombre
                var name = user.Identity?.Name;

                // Obtiene el rol
                var role = user.Claims.FirstOrDefault(x => x.Type == ClaimTypes.Role)?.Value;

                // Retorna la data
                return Results.Ok(new
                {
                    user = name,
                    role
                });
            }).RequireAuthorization();

            app.UseSwagger();
            app.UseSwaggerUI();

            app.Run();
        }
    }
}
