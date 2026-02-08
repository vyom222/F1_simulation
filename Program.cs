using F1_simulation;
using F1_simulation.Database;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;

var builder = WebApplication.CreateBuilder(args);

// Database connection string - update password as needed
var connectionString = "server=127.0.0.1;port=3306;database=F1;user=root;password=bluedog13;";

// Add services
builder.Services.AddSingleton(new F1_cache(connectionString));
builder.Services.AddControllers();
builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowAll", policy =>
    {
        policy.AllowAnyOrigin()
            .AllowAnyMethod()
            .AllowAnyHeader();
    });
});
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

var app = builder.Build();

// Get the base directory
var baseDir = Directory.GetCurrentDirectory();
var staticDir = Path.Combine(baseDir, "Static");
var templatesDir = Path.Combine(baseDir, "Templates");

// Configure middleware
app.UseSwagger();
app.UseSwaggerUI();

app.UseCors("AllowAll");
app.UseAuthorization();

// Serve static files
app.UseStaticFiles(new StaticFileOptions
{
    FileProvider = new PhysicalFileProvider(staticDir),
    RequestPath = "/static"
});

// Route API controllers
app.MapControllers();

// Serve index.html for root and any unmatched routes
app.MapGet("/", async context =>
{
    var indexPath = Path.Combine(templatesDir, "index.html");
    if (File.Exists(indexPath))
    {
        context.Response.ContentType = "text/html";
        await context.Response.WriteAsync(File.ReadAllText(indexPath));
    }
    else
    {
        context.Response.StatusCode = 404;
        await context.Response.WriteAsync("index.html not found");
    }
});

Console.WriteLine("Starting F1 Simulation Web Application on http://localhost:5000");
app.Run("http://localhost:5000");
