using System;
using System.Threading.RateLimiting;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.OpenApi.Models;

namespace AspDotNetCore_Rate_Limiting
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

            services.AddHttpContextAccessor();
            services.AddControllers();
            services.AddSwaggerGen(c =>
            {
                c.SwaggerDoc("v1", new OpenApiInfo { Title = "AspDotNetCore Rate Limiting", Version = "v1" });
            });

            const string fixedPolicy = "fixed";
            const string slidingPolicy = "sliding";
            const string concurrencyPolicy = "concurrency";
            const string bucketPolicy = "bucket";
            services.AddRateLimiter(rateLimiter =>
            {

                //rateLimiter.RejectionStatusCode = StatusCodes.Status429TooManyRequests;

                //global rate limiter for all requests, per authenticated username (or hostname if not authenticated)
                //rateLimiter.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(httpContext =>
                //    RateLimitPartition.GetFixedWindowLimiter(
                //        partitionKey: httpContext.User.Identity?.Name ?? httpContext.Request.Headers.Host.ToString(),
                //        factory: partition => new FixedWindowRateLimiterOptions
                //        {
                //            AutoReplenishment = true,
                //            PermitLimit = 10,
                //            QueueLimit = 0,
                //            Window = TimeSpan.FromMinutes(1)
                //        }));

                //can “chain” rate limiters of one type of various types, using the PartitionedRateLimiter.CreateChained()
                //rateLimiter.GlobalLimiter = PartitionedRateLimiter.CreateChained(
                //    PartitionedRateLimiter.Create<HttpContext, string>(httpContext =>
                //        RateLimitPartition.GetFixedWindowLimiter(httpContext.Connection.RemoteIpAddress?.ToString(), partition =>
                //            new FixedWindowRateLimiterOptions
                //            {
                //                AutoReplenishment = true,
                //                PermitLimit = 6,
                //                Window = TimeSpan.FromMinutes(1)
                //            })),
                //    PartitionedRateLimiter.Create<HttpContext, string>(httpContext =>
                //        RateLimitPartition.GetFixedWindowLimiter(httpContext.Connection.RemoteIpAddress?.ToString(), partition =>
                //            new FixedWindowRateLimiterOptions
                //            {
                //                AutoReplenishment = true,
                //                PermitLimit = 60,
                //                Window = TimeSpan.FromHours(1)
                //            })));

                rateLimiter.AddFixedWindowLimiter(policyName: fixedPolicy, options =>
                {
                    //The time window for rate limiting (e.g., 5 seconds).
                    options.Window = TimeSpan.FromSeconds(5);
                    //The maximum number of permits allowed within the window (e.g., 3)
                    options.PermitLimit = 3;
                    //The maximum number of requests that can be queued when the limit is reached(e.g., 2).
                    options.QueueLimit = 0;
                    //The order in which requests are processed from the queue (e.g., oldest requests first).
                    options.QueueProcessingOrder = QueueProcessingOrder.OldestFirst;
                    options.AutoReplenishment = true;

                });

                rateLimiter.AddSlidingWindowLimiter(policyName: slidingPolicy, options =>
                {
                    //The time window for rate limiting (e.g., 5 seconds).
                    options.Window = TimeSpan.FromSeconds(10);
                    //The maximum number of permits allowed within the window (e.g., 3)
                    options.PermitLimit = 10;
                    //The maximum number of requests that can be queued when the limit is reached(e.g., 2).
                    options.QueueLimit = 5;
                    //The order in which requests are processed from the queue (e.g., oldest requests first).
                    options.QueueProcessingOrder = QueueProcessingOrder.OldestFirst;
                    options.SegmentsPerWindow = 5;
                });

                rateLimiter.AddConcurrencyLimiter(policyName: concurrencyPolicy, options =>
                {
                    options.PermitLimit = 10;
                    options.QueueProcessingOrder = QueueProcessingOrder.OldestFirst;
                    options.QueueLimit = 4;
                });

                rateLimiter.AddTokenBucketLimiter(policyName: bucketPolicy, options =>
                {
                    options.TokenLimit = 15;
                    options.QueueProcessingOrder = QueueProcessingOrder.OldestFirst;
                    options.QueueLimit = 5;
                    options.ReplenishmentPeriod = TimeSpan.FromSeconds(10);
                    options.TokensPerPeriod = 5;
                    options.AutoReplenishment = true;
                });

                //sets the response status code to 429, and returns a meaningful response
                rateLimiter.OnRejected = (context, token) =>
                {
                    context.HttpContext.Response.StatusCode = StatusCodes.Status429TooManyRequests;

                    var httpContext = context.HttpContext;
                    var ipAddress = httpContext.Connection.RemoteIpAddress;
                    var url = httpContext.Request.Path;
                    var method = httpContext.Request.Method;
                    var errorMessage = $"Too many requests from {ipAddress} to {url} using {method}.";

                    if (context.Lease.TryGetMetadata(MetadataName.RetryAfter, out var retryAfter))
                    {

                        context.HttpContext.RequestServices.GetService<ILoggerFactory>()?
                            .CreateLogger("Microsoft.AspNetCore.RateLimitingMiddleware")
                            .LogWarning(errorMessage);

                        context.HttpContext.Response.WriteAsync(
                            $"Too many requests. Please try again after {retryAfter.TotalMinutes} minute(s). " +
                            $"Read more about our rate limits at https://example.org/docs/ratelimiting.", cancellationToken: token);
                        return new ValueTask();
                    }
                    else
                    {
                        context.HttpContext.RequestServices.GetService<ILoggerFactory>()?
                            .CreateLogger("Microsoft.AspNetCore.RateLimitingMiddleware")
                            .LogWarning(errorMessage);
                        context.HttpContext.Response.WriteAsync(
                            "Too many requests. Please try again later. " +
                            "Read more about our rate limits at https://example.org/docs/ratelimiting.", cancellationToken: token);
                        return new ValueTask();
                    }
                };
            });
        }

        public void Configure(IApplicationBuilder app, IWebHostEnvironment env)
        {
            if (env.IsDevelopment())
            {
                app.UseDeveloperExceptionPage();
            }

            app.UseSwagger();
            app.UseSwaggerUI(o =>
            {
                o.SwaggerEndpoint("/swagger/v1/swagger.json", "AspDotNetCore Rate Limiting");
                o.RoutePrefix = string.Empty;
            });

            app.UseHttpsRedirection();
            app.UseRouting();
            app.UseAuthorization();
            app.UseHttpLogging();
            app.UseRateLimiter();
            app.UseEndpoints(endpoints =>
            {
                endpoints.MapControllers();
            });
        }
    }
}