using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Options;
var builder = WebApplication.CreateBuilder(args);
builder.Host.UseWindowsService();

builder.Services.AddRazorPages();
builder.Services.AddHttpContextAccessor();
builder.Services.AddHttpClient();
builder.Services.AddScoped<InventarioWeb.Services.AuthService>();
builder.Services.AddDistributedMemoryCache();
builder.Services.AddSession (options =>
{
    options.IdleTimeout =
TimeSpan.FromHours(8); 
    options.Cookie.HttpOnly = true;
    options.Cookie.IsEssential = true;
});

var app = builder.Build();

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error");
    // app.UseHsts();  // No necesario en intranet
}

app.UseStaticFiles();

app.UseRouting();

if (app.Configuration.GetValue<bool>("DemoMode"))
{
    app.Use(async (context, next) =>
    {
        if (context.Request.Method == "POST")
        {
            var referer = context.Request.Headers.Referer.ToString();
            var redirectUrl = string.IsNullOrEmpty(referer) ? "/" : referer;
            var separator = redirectUrl.Contains('?') ? "&" : "?";
            context.Response.Redirect(redirectUrl + separator + "demo=1");
            return;
        }
        await next(context);
    });
}

app.UseSession();
app.MapRazorPages();

// Health check: verifica conexión a SQL Server
app.MapGet("/api/health", async (IConfiguration config) =>
{
    try
    {
        using var conn = new SqlConnection(config.GetConnectionString("SqlServer"));
        await conn.OpenAsync();
        return Results.Ok(new { ok = true, ts = DateTime.Now });
    }
    catch (Exception ex)
    {
        return Results.Ok(new { ok = false, error = ex.Message, ts = DateTime.Now });
    }
});

app.Run();
