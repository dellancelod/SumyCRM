using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using SumyCRM.Data;
using SumyCRM.Services;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services.AddControllersWithViews();

builder.Services.AddDbContext<AppDbContext>(options => options
    .UseMySql(
        builder.Configuration.GetConnectionString("MariaDbConnectionString"),
        new MariaDbServerVersion(new Version(11, 1, 0))
    ));


//Configure Identity system
builder.Services.AddIdentity<IdentityUser, IdentityRole>(options => {
    options.Password.RequiredLength = 6;
    options.Password.RequireNonAlphanumeric = false;
    options.Password.RequireDigit = false;
    options.Password.RequireLowercase = false;
    options.Password.RequireUppercase = false;
}).AddEntityFrameworkStores<AppDbContext>()
.AddDefaultTokenProviders();
//Authentication cookies
builder.Services.ConfigureApplicationCookie(options => {
    options.Cookie.Name = "sumyCrmAuth";
    options.Cookie.HttpOnly = true;
    options.LoginPath = "/home/index";
    options.AccessDeniedPath = "/home/accessdenied";
    options.SlidingExpiration = true;
});

//Configure Authorization policy for admin area
builder.Services.AddAuthorization(x => {
    x.AddPolicy("AdminArea", policy => {
        policy.RequireRole("admin");
    });
});

// Add services to the container.
builder.Services.AddControllersWithViews(x =>
{
    x.Conventions.Add(new AdminAreaAuthorization("Admin", "AdminArea"));
});
var app = builder.Build();

// Configure the HTTP request pipeline.
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Home/Error");
    // The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
    app.UseHsts();
}

app.UseHttpsRedirection();
app.UseStaticFiles();

app.UseRouting();
app.UseCookiePolicy();
app.UseAuthentication();
app.UseAuthorization();

app.UseEndpoints((endpoints) => {
    endpoints.MapControllerRoute(
        name: "admin",
        pattern: "{area:exists}/{controller=Home}/{action=Index}/{id?}"
    );
    endpoints.MapControllerRoute(
        name: "default",
        pattern: "{controller=Home}/{action=Index}/{id?}"
    );
});


app.UseAuthorization();

app.Run();
