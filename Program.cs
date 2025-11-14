using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using FoodGapp;
using FoodGappBackend_WebAPI.Data;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllersWithViews();

//builder.Services.AddDbContext<FoodGappDbContext>(options =>
//{
//    var connectionString = builder.Configuration.GetConnectionString("RemoteConnection");
//    options.UseSqlServer(connectionString);
//});
builder.Services.AddDbContext<FoodGappDbContext>(options =>
    options.UseMySql(
        builder.Configuration.GetConnectionString("DefaultConnection"),
        new MySqlServerVersion(new Version(8, 0, 36))
    ));


builder.Services.AddAuthentication(
    CookieAuthenticationDefaults.AuthenticationScheme
    ).AddCookie(option => {
        option.ExpireTimeSpan = TimeSpan.FromMinutes(60);
    });

builder.Services.AddScoped<IAuthorizationHandler, RolesInDBAuthorizationHandler>();

builder.Services.AddAuthorization(options =>
{
    options.AddPolicy("SuperAdminPolicy", policy =>
        policy.RequireRole("super admin"));

    options.AddPolicy("AdminPolicy", policy =>
        policy.RequireRole("admin"));

    options.AddPolicy("AdminOrSuperAdminPolicy", policy =>
        policy.RequireAssertion(context =>
            context.User.IsInRole("admin") || context.User.IsInRole("super admin")));
});

builder.Services.AddHttpClient();

builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowAll", builder =>
    {
        builder.AllowAnyOrigin()
               .AllowAnyMethod()
               .AllowAnyHeader();
    });
});

var app = builder.Build();
app.UseCors("AllowAll");

app.UseStaticFiles();

app.UseRouting();

app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<FoodGappDbContext>();
    try
    {
        db.Database.CanConnect();
        Console.WriteLine("Connected to Railway MySQL successfully!");
    }
    catch (Exception ex)
    {
        Console.WriteLine($"DB Connection failed: {ex.Message}");
    }
}
//app.MapControllerRoute();

app.Run();