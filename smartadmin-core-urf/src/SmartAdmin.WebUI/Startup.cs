﻿using System;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using DotNetCore.CAP;
using DotNetCore.CAP.Messages;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Connections;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.UI.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi.Models;
using SmartAdmin.Data.Models;
using SmartAdmin.Repository;
using SmartAdmin.Service;
using SmartAdmin.Service.Helper;
using SmartAdmin.WebUI.Data;
using SmartAdmin.WebUI.Data.Models;
using SmartAdmin.WebUI.Hubs;
using SmartAdmin.WebUI.Models;
using SqlSugar;
using URF.Core.Abstractions;
using URF.Core.Abstractions.Trackable;
using URF.Core.EF;
using URF.Core.EF.Trackable;

namespace SmartAdmin.WebUI
{
  public class Startup
  {
    

    public Startup(IConfiguration configuration)
    {
      Configuration = configuration;

    }

    public IConfiguration Configuration { get; }

    // This method gets called by the runtime. Use this method to add services to the container.
    public void ConfigureServices(IServiceCollection services)
    {
      services.Configure<SmartSettings>(Configuration.GetSection(SmartSettings.SectionName));
      var settings = Configuration.GetSection(nameof(SmartSettings)).Get<SmartSettings>();
      // Note: This line is for demonstration purposes only, I would not recommend using this as a shorthand approach for accessing settings
      // While having to type '.Value' everywhere is driving me nuts (>_<), using this method means reloaded appSettings.json from disk will not work
      services.AddSingleton(s => s.GetRequiredService<IOptions<SmartSettings>>().Value);

      services.Configure<CookiePolicyOptions>(options =>
      {
        // This lambda determines whether user consent for non-essential cookies is needed for a given request.
        options.CheckConsentNeeded = context => true;
        options.MinimumSameSitePolicy = SameSiteMode.None;
      });
      var connectionString = Configuration.GetConnectionString(nameof(SmartDbContext));

      services.AddDbContext<ApplicationDbContext>(options => options.UseSqlServer(connectionString, sqlServerOptions=> {
        sqlServerOptions.CommandTimeout(60);
        })
      .UseQueryTrackingBehavior(QueryTrackingBehavior.NoTracking)
      );
      services.AddDbContext<SmartDbContext>(options => options.UseSqlServer(connectionString));
      services.AddIdentity<ApplicationUser, IdentityRole>(options =>
      {
        // Password settings.
        options.Password.RequiredLength = 4;
        options.Password.RequireLowercase = false;
        options.Password.RequireUppercase = false;
        options.Password.RequireNonAlphanumeric = false;
        options.Password.RequireDigit = false;

        // Lockout settings.
        options.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(15);
        options.Lockout.MaxFailedAccessAttempts = 5;
        options.Lockout.AllowedForNewUsers = true;
        // User settings.
        options.User.AllowedUserNameCharacters = "abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789-._@+";
        options.User.RequireUniqueEmail = true;
      })
                .AddRoleManager<RoleManager<IdentityRole>>()
                .AddEntityFrameworkStores<ApplicationDbContext>()
                .AddDefaultTokenProviders();

      services.AddSingleton<IHttpContextAccessor, HttpContextAccessor>();
      services.AddScoped(SqlSugarFactory.CreateSqlSugarClient);
      #region infrastructure framework
      services.AddScoped<DbContext, SmartDbContext>();
      services.AddScoped<IUnitOfWork, UnitOfWork>();
      services.AddScoped<ITrackableRepository<DataTableImportMapping>, TrackableRepository<DataTableImportMapping>>();
      services.AddScoped<IDataTableImportMappingService, DataTableImportMappingService>();
      services.AddScoped<ITrackableRepository<CodeItem>, TrackableRepository<CodeItem>>();
      services.AddScoped<ICodeItemService, CodeItemService>();
      services.AddScoped<ITrackableRepository<MenuItem>, TrackableRepository<MenuItem>>();
      services.AddScoped<IMenuItemService, MenuItemService>();
      services.AddScoped<ITrackableRepository<Notification>, TrackableRepository<Notification>>();
      services.AddScoped<INotificationService, NotificationService>();
      services.AddScoped<ITrackableRepository<RoleMenu>, TrackableRepository<RoleMenu>>();
      services.AddScoped<IRoleMenuService, RoleMenuService>();
      #endregion

      #region 注入业务服务
      services.AddScoped<ITrackableRepository<Company>, TrackableRepository<Company>>();
      services.AddScoped<ICompanyService, CompanyService>();
      services.AddScoped<ITrackableRepository<Product>, TrackableRepository<Product>>();
      services.AddScoped<IProductService, ProductService>();
      services.AddScoped<IRepositoryX<Customer>, RepositoryX<Customer>>();
      services.AddScoped<ICustomerService, CustomerService>();
      #endregion
      services.AddTransient<IEmailSender, EmailSender>();

      services
          .AddControllersWithViews(options =>
          {
            options.Filters.Add(typeof(ViewBagFilter));
          })
          .AddJsonOptions(
        opts =>
        {
          opts.JsonSerializerOptions.PropertyNamingPolicy = null;
          opts.JsonSerializerOptions.IgnoreNullValues = true;
        });



      services.AddRazorPages();
      services.AddMvc().AddRazorRuntimeCompilation();

      //Jwt Authentication
      services.AddAuthentication(opts =>
      {
        //opts.DefaultAuthenticateScheme = CookieAuthenticationDefaults.AuthenticationScheme;
        //opts.DefaultChallengeScheme = JwtBearerDefaults.AuthenticationScheme;
      })
        .AddPolicyScheme(settings.App, "Bearer or Jwt", options =>
        {
          options.ForwardDefaultSelector = context =>
          {
            var bearerAuth = context.Request.Headers["Authorization"].FirstOrDefault()?.StartsWith("Bearer ") ?? false;
            // You could also check for the actual path here if that's your requirement:
            // eg: if (context.HttpContext.Request.Path.StartsWithSegments("/api", StringComparison.InvariantCulture))
            if (bearerAuth)
              return JwtBearerDefaults.AuthenticationScheme;
            else
              return CookieAuthenticationDefaults.AuthenticationScheme;
          };
        })
        .AddCookie(CookieAuthenticationDefaults.AuthenticationScheme, options =>
       {
         options.LoginPath = "/Identity/Account/Login";
         options.LogoutPath = "/Identity/Account/Logout";
         options.AccessDeniedPath = "/Identity/Account/AccessDenied";
         options.Cookie.Name = "CustomerPortal.Identity";
         options.SlidingExpiration = true;
         options.ExpireTimeSpan = TimeSpan.FromDays(30); //Account.Login overrides this default value
       })
        .AddJwtBearer(JwtBearerDefaults.AuthenticationScheme,x =>
      {
        x.RequireHttpsMetadata = false;
        x.SaveToken = true;
        x.TokenValidationParameters = new TokenValidationParameters
        {
          ValidateIssuerSigningKey = true,
          IssuerSigningKey = new SymmetricSecurityKey(Encoding.ASCII.GetBytes(Configuration["Jwt:Key"])),
          ValidateIssuer = true,
          ValidateAudience = true,
          ValidateLifetime = true,
          ValidIssuer = Configuration["Jwt:Issuer"],
          ValidAudience = Configuration["Jwt:Issuer"],
        };
      });

 
      services.ConfigureApplicationCookie(options =>
      {
        // Cookie settings
        options.Cookie.Name = settings.App;
        options.Cookie.HttpOnly = true;
        options.ExpireTimeSpan = TimeSpan.FromDays(30);
        options.LoginPath = "/Identity/Account/Login";
        options.LogoutPath = "/Identity/Account/Logout";
        options.Events = new CookieAuthenticationEvents()
        {
          OnRedirectToLogin = context =>
          {
            if (context.Request.Path.Value.StartsWith("/api"))
            {
              context.Response.Clear();
              context.Response.StatusCode = 401;
              return Task.FromResult(0);
            }
            context.Response.Redirect(context.RedirectUri);
            return Task.FromResult(0);
          }
        };
        //options.AccessDeniedPath = "/Identity/Account/AccessDenied";
      });
      // Register the Swagger generator
      services.AddSwaggerGen(c =>
      {
        c.SwaggerDoc("v1", new OpenApiInfo { Title = settings.AppName, Version = settings.Version });
        c.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme
        {
          Name = "Authorization",
          Type = SecuritySchemeType.ApiKey,
          Scheme = "Bearer",
          BearerFormat = "JWT",
          In = ParameterLocation.Header,
          Description = "JWT Authorization header using the Bearer scheme."
        });
        c.AddSecurityRequirement(new OpenApiSecurityRequirement
                {
                    {
                          new OpenApiSecurityScheme
                            {
                                Reference = new OpenApiReference
                                {
                                    Type = ReferenceType.SecurityScheme,
                                    Id = "Bearer"
                                }
                            },
                            new string[] {}

                    }
                });
      });

      var mqoptions= Configuration.GetSection(nameof(RabbitMQOptions)).Get<RabbitMQOptions>();
      services.AddCap(x =>
      {
        x.UseEntityFramework<SmartDbContext>();
        x.UseRabbitMQ(options=> {
          options = mqoptions;
          });
        x.UseDashboard( options=> {
          options.Authorization= new[] { new DashboardAuthorizationFilter() };
        });
        x.FailedRetryCount = 5;
        x.FailedThresholdCallback = failed =>
        {
          var logger = failed.ServiceProvider.GetService<ILogger<Startup>>();
          logger.LogError($@"A message of type {failed.MessageType} failed after executing {x.FailedRetryCount} several times, 
                        requiring manual troubleshooting. Message name: {failed.Message.GetName()}");
        };
      });
      services.AddSignalR(options =>
      {
        options.EnableDetailedErrors = true;
        options.KeepAliveInterval = TimeSpan.FromMinutes(1);
      });
     }

    // This method gets called by the runtime. Use this method to configure the HTTP request pipeline.
    public void Configure(IApplicationBuilder app, IWebHostEnvironment env, ILogger<Startup> logger)
    {
  
      //Use the EF Core DB Context Service to automatically migrate database changes
      using (var serviceScope = app.ApplicationServices.CreateScope())
      {
        var context = serviceScope.ServiceProvider.GetService<SmartDbContext>();
        if (context.Database.GetPendingMigrations().Any())
        {
          logger.LogInformation("SmartDbContext:执行数据库迁移");
          context.Database.Migrate();
        }
        var identitycontext = serviceScope.ServiceProvider.GetService<ApplicationDbContext>();
        if (identitycontext.Database.GetPendingMigrations().Any())
        {
          logger.LogInformation("ApplicationDbContext:执行数据库迁移");
          identitycontext.Database.Migrate();
        }
      }
      if (env.IsDevelopment())
      {
        app.UseDeveloperExceptionPage();
        app.UseDatabaseErrorPage();
      }
      else
      {
        app.UseExceptionHandler("/Page/Error");
        // The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
        app.UseHsts();
      }
      app.UseHttpsRedirection();
      app.UseStaticFiles();

      // Enable middleware to serve generated Swagger as a JSON endpoint.
      app.UseSwagger();

      // Enable middleware to serve swagger-ui (HTML, JS, CSS, etc.),
      // specifying the Swagger JSON endpoint.
      app.UseSwaggerUI(c =>
      {
        c.SwaggerEndpoint("/swagger/v1/swagger.json", "SmartAdmin.WebApi");
      });

      app.UseRouting();

      app.UseAuthentication();
      app.UseAuthorization();


      app.UseEndpoints(endpoints =>
      {
        endpoints.MapControllerRoute(
                  "default",
                  "{controller=Home}/{action=Index}/{id?}");
        endpoints.MapRazorPages();
        endpoints.MapHub<NotificationHub>("/NotificationHub",options=> {
          options.Transports = HttpTransportType.LongPolling;
          });
      });
      
      logger.LogInformation("网站启动");
    }
  }
}
