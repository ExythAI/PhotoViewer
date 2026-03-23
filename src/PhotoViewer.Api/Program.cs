using System.Text;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using PhotoViewer.Api.Data;
using PhotoViewer.Api.Services;

var builder = WebApplication.CreateBuilder(args);

// Configure SQLite
var dbPath = builder.Configuration["Storage:DatabasePath"] ?? "/data/photoviewer.db";
var dbDir = Path.GetDirectoryName(dbPath);
if (dbDir != null) Directory.CreateDirectory(dbDir);

builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseSqlite($"Data Source={dbPath}"));

// Configure JWT Authentication
var jwtKey = builder.Configuration["Jwt:Key"] ?? "PhotoViewerDefaultSecretKey2026!@#$%^&*()";
builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidateAudience = true,
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,
            ValidIssuer = builder.Configuration["Jwt:Issuer"] ?? "PhotoViewer",
            ValidAudience = builder.Configuration["Jwt:Audience"] ?? "PhotoViewer",
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtKey))
        };
    });

builder.Services.AddAuthorization();
builder.Services.AddControllers();

// Register services
builder.Services.AddScoped<AuthService>();
builder.Services.AddSingleton<ThumbnailService>();
builder.Services.AddSingleton<DownloadService>();
builder.Services.AddSingleton<FileIndexerService>();
builder.Services.AddHostedService(sp => sp.GetRequiredService<FileIndexerService>());

// CORS for development
builder.Services.AddCors(options =>
{
    options.AddDefaultPolicy(policy =>
    {
        policy.AllowAnyOrigin().AllowAnyMethod().AllowAnyHeader();
    });
});

var app = builder.Build();

// Apply migrations
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    db.Database.Migrate();
}

app.UseCors();
app.UseAuthentication();
app.UseAuthorization();

// Serve SPA static files
app.UseDefaultFiles();
app.UseStaticFiles();

app.MapControllers();

// SPA fallback - serve index.html for any non-API, non-file route
app.MapFallbackToFile("index.html");

app.Run();
