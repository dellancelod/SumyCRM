using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.FileProviders;
using SumyCRM.Data;
using SumyCRM.Data.Repository.EntityFramework;
using SumyCRM.Data.Repository.Interfaces;
using SumyCRM.Services;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services.AddControllersWithViews();

builder.Services.AddTransient<IRequestsRepository, EFRequestsRepository>();
builder.Services.AddTransient<ICategoriesRepository, EFCategoriesRepository>();
builder.Services.AddTransient<ISchedulesRepository, EFSchedulesRepository>();
builder.Services.AddTransient<IFacilitiesRepository, EFFacilitiesRepository>();
builder.Services.AddTransient<IUserFacilities, EFUserFacilities>();
builder.Services.AddTransient<ICallRecordingsRepository, EFCallRecordingsRepository>();
builder.Services.AddTransient<IWaterLeakReports, EFWaterLeakReportsRepository>();
builder.Services.AddTransient<ICallEventsRepository, EFCallEventsRepository>();

builder.Services.AddHttpClient<IScheduleAudioService, ScheduleAudioService>();
builder.Services.AddHttpClient<IGeocodingService, NominatimGeocodingService>();
builder.Services.AddScoped<IGeocodingService, NominatimGeocodingService>();

builder.Services.AddTransient<DataManager>();
builder.Services.AddDbContext<AppDbContext>(options => options
    .UseMySql(
        builder.Configuration.GetConnectionString("MariaDbConnectionString"),
        new MariaDbServerVersion(new Version(11, 1, 0))
    ));

builder.Services.AddMemoryCache();

builder.Services.AddHttpClient("nominatim", client =>
{
    client.BaseAddress = new Uri("https://nominatim.openstreetmap.org/");
    client.Timeout = TimeSpan.FromSeconds(10);
    client.DefaultRequestHeaders.UserAgent.ParseAdd("SumyCRM/1.0 (contact: admin@giftsbakery.com.ua)");
});

builder.Services.AddHttpClient("overpass", client =>
{
    client.Timeout = TimeSpan.FromSeconds(25);
    client.DefaultRequestHeaders.UserAgent.ParseAdd("SumyCRM/1.0 (contact: admin@giftsbakery.com.ua)");
});

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
        policy.RequireRole("admin", "operator");
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

app.UseStaticFiles(new StaticFileOptions
{
    FileProvider = new PhysicalFileProvider("/usr/share/asterisk/sounds/en"),
    RequestPath = "/audio/schedules"
});

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
