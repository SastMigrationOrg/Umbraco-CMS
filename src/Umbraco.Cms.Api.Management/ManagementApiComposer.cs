using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ApiExplorer;
using Microsoft.AspNetCore.Mvc.Versioning;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Microsoft.OpenApi.Models;
using Umbraco.Cms.Core;
using Umbraco.Cms.Core.Composing;
using Umbraco.Cms.Core.Configuration.Models;
using Umbraco.Cms.Core.DependencyInjection;
using Umbraco.Cms.Api.Common.Configuration;
using Umbraco.Cms.Api.Common.DependencyInjection;
using Umbraco.Cms.Api.Management.Controllers.Security;
using Umbraco.Cms.Api.Management.DependencyInjection;
using Umbraco.Cms.Api.Management.OpenApi;
using Umbraco.Cms.Infrastructure.Serialization;
using Umbraco.Cms.Web.Common.ApplicationBuilder;
using Umbraco.Extensions;
using Umbraco.New.Cms.Core.Models.Configuration;
using IHostingEnvironment = Umbraco.Cms.Core.Hosting.IHostingEnvironment;

namespace Umbraco.Cms.Api.Management;

public class ManagementApiComposer : IComposer
{
    private const string ApiTitle = "Umbraco Backoffice API";
    private const string ApiDefaultDocumentName = "v1";

    private ApiVersion DefaultApiVersion => new ApiVersion(1, 0);

    public void Compose(IUmbracoBuilder builder)
    {
        // TODO Should just call a single extension method that can be called fromUmbracoTestServerTestBase too, instead of calling this method

        IServiceCollection services = builder.Services;

        builder
            .AddNewInstaller()
            .AddUpgrader()
            .AddSearchManagement()
            .AddFactories()
            .AddTrees()
            .AddFactories()
            .AddServices()
            .AddMappers()
            .AddBackOfficeAuthentication();

        services.ConfigureOptions<ConfigureApiVersioningOptions>();
        services.AddApiVersioning();

        services.AddSwaggerGen(swaggerGenOptions =>
        {
            swaggerGenOptions.CustomOperationIds(e =>
            {
                var httpMethod = e.HttpMethod?.ToLower().ToFirstUpper() ?? "Get";

                // if the route info "Name" is supplied we'll use this explicitly as the operation ID
                // - usage example: [HttpGet("my-api/route}", Name = "MyCustomRoute")]
                if (string.IsNullOrWhiteSpace(e.ActionDescriptor.AttributeRouteInfo?.Name) == false)
                {
                    var explicitOperationId = e.ActionDescriptor.AttributeRouteInfo!.Name;
                    return explicitOperationId.InvariantStartsWith(httpMethod)
                        ? explicitOperationId
                        : $"{httpMethod}{explicitOperationId}";
                }

                var relativePath = e.RelativePath;

                if (string.IsNullOrWhiteSpace(relativePath))
                {
                    throw new Exception(
                        $"There is no relative path for controller action {e.ActionDescriptor.RouteValues["controller"]}");
                }

                // Remove the prefixed base path with version, e.g. /umbraco/management/api/v1/tracked-reference/{id} => tracked-reference/{id}
                var unprefixedRelativePath =
                    OperationIdRegexes.VersionPrefixRegex().Replace(relativePath, string.Empty);

                // Remove template placeholders, e.g. tracked-reference/{id} => tracked-reference/Id
                var formattedOperationId = OperationIdRegexes.TemplatePlaceholdersRegex()
                    .Replace(unprefixedRelativePath, m => $"By{m.Groups[1].Value.ToFirstUpper()}");

                // Remove dashes (-) and slashes (/) and convert the following letter to uppercase with
                // the word "By" in front, e.g. tracked-reference/Id => TrackedReferenceById
                formattedOperationId = OperationIdRegexes.ToCamelCaseRegex()
                    .Replace(formattedOperationId, m => m.Groups[1].Value.ToUpper());

                // Return the operation ID with the formatted http method verb in front, e.g. GetTrackedReferenceById
                return $"{httpMethod}{formattedOperationId.ToFirstUpper()}";
            });
            swaggerGenOptions.SwaggerDoc(
                ApiDefaultDocumentName,
                new OpenApiInfo
                {
                    Title = ApiTitle,
                    Version = DefaultApiVersion.ToString(),
                    Description =
                        "This shows all APIs available in this version of Umbraco - including all the legacy apis that are available for backward compatibility"
                });

            swaggerGenOptions.DocInclusionPredicate((_, api) => !string.IsNullOrWhiteSpace(api.GroupName));

            swaggerGenOptions.TagActionsBy(api => new[] { api.GroupName });

            // see https://github.com/domaindrivendev/Swashbuckle.AspNetCore#change-operation-sort-order-eg-for-ui-sorting
            string ActionSortKeySelector(ApiDescription apiDesc)
            {
                return
                    $"{apiDesc.GroupName}_{apiDesc.ActionDescriptor.AttributeRouteInfo?.Template ?? apiDesc.ActionDescriptor.RouteValues["controller"]}_{apiDesc.ActionDescriptor.RouteValues["action"]}_{apiDesc.HttpMethod}";
            }

            swaggerGenOptions.OrderActionsBy(ActionSortKeySelector);

            swaggerGenOptions.AddSecurityDefinition(
                "OAuth",
                new OpenApiSecurityScheme
                {
                    In = ParameterLocation.Header,
                    Name = "Umbraco",
                    Type = SecuritySchemeType.OAuth2,
                    Description = "Umbraco Authentication",
                    Flows = new OpenApiOAuthFlows
                    {
                        AuthorizationCode = new OpenApiOAuthFlow
                        {
                            AuthorizationUrl =
                                new Uri(Paths.BackOfficeApiAuthorizationEndpoint, UriKind.Relative),
                            TokenUrl = new Uri(Paths.BackOfficeApiTokenEndpoint, UriKind.Relative)
                        }
                    }
                });

            swaggerGenOptions.AddSecurityRequirement(new OpenApiSecurityRequirement
            {
                // this weird looking construct works because OpenApiSecurityRequirement
                // is a specialization of Dictionary<,>
                {
                    new OpenApiSecurityScheme
                    {
                        Reference = new OpenApiReference { Id = "OAuth", Type = ReferenceType.SecurityScheme }
                    },
                    new List<string>()
                }
            });

            swaggerGenOptions.DocumentFilter<MimeTypeDocumentFilter>();
            swaggerGenOptions.SchemaFilter<EnumSchemaFilter>();

            swaggerGenOptions.CustomSchemaIds(SchemaIdGenerator.Generate);
        });


        services.ConfigureOptions<ConfigureApiExplorerOptions>();
        services.AddVersionedApiExplorer();
        services.AddControllers()
            .AddJsonOptions(options =>
            {
                // any generic JSON options go here
            })
            .AddJsonOptions(Umbraco.New.Cms.Core.Constants.JsonOptionsNames.BackOffice, options =>
            {
                // all back-office specific JSON options go here
                options.JsonSerializerOptions.PropertyNamingPolicy = JsonNamingPolicy.CamelCase;
                options.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter());
                options.JsonSerializerOptions.Converters.Add(new JsonObjectConverter());
            });
        builder.Services.ConfigureOptions<ConfigureMvcOptions>();

        // TODO: when this is moved to core, make the AddUmbracoOptions extension private again and remove core InternalsVisibleTo for Umbraco.Cms.Api.Management
        builder.AddUmbracoOptions<NewBackOfficeSettings>();
        builder.Services.AddSingleton<IValidateOptions<NewBackOfficeSettings>, NewBackOfficeSettingsValidator>();

        builder.Services.Configure<UmbracoPipelineOptions>(options =>
        {
            options.AddFilter(new UmbracoPipelineFilter(
                "BackofficeSwagger",
                applicationBuilder =>
                {
                    // Only use the API exception handler when we are requesting an API
                    applicationBuilder.UseWhen(
                        httpContext =>
                        {
                            GlobalSettings? settings = httpContext.RequestServices
                                .GetRequiredService<IOptions<GlobalSettings>>().Value;
                            IHostingEnvironment hostingEnvironment =
                                httpContext.RequestServices.GetRequiredService<IHostingEnvironment>();
                            var officePath = settings.GetBackOfficePath(hostingEnvironment);

                            return httpContext.Request.Path.Value?.StartsWith($"{officePath}/management/api/") ?? false;
                        },
                        innerBuilder =>
                        {
                            innerBuilder.UseExceptionHandler(exceptionBuilder => exceptionBuilder.Run(async context =>
                            {
                                Exception? exception = context.Features.Get<IExceptionHandlerPathFeature>()?.Error;
                                if (exception is null)
                                {
                                    return;
                                }

                                var response = new ProblemDetails
                                {
                                    Title = exception.Message,
                                    Detail = exception.StackTrace,
                                    Status = StatusCodes.Status500InternalServerError,
                                    Instance = exception.GetType().Name,
                                    Type = "Error"
                                };
                                await context.Response.WriteAsJsonAsync(response);
                            }));
                        });
                },
                applicationBuilder =>
                {
                    IServiceProvider provider = applicationBuilder.ApplicationServices;
                    IWebHostEnvironment webHostEnvironment = provider.GetRequiredService<IWebHostEnvironment>();

                    if (!webHostEnvironment.IsProduction())
                    {
                        GlobalSettings? settings = provider.GetRequiredService<IOptions<GlobalSettings>>().Value;
                        IHostingEnvironment hostingEnvironment = provider.GetRequiredService<IHostingEnvironment>();
                        var officePath = settings.GetBackOfficePath(hostingEnvironment);

                        applicationBuilder.UseSwagger(swaggerOptions =>
                        {
                            swaggerOptions.RouteTemplate =
                                $"{officePath.TrimStart(Constants.CharArrays.ForwardSlash)}/swagger/{{documentName}}/swagger.json";
                        });
                        applicationBuilder.UseSwaggerUI(
                            swaggerUiOptions =>
                        {
                            swaggerUiOptions.SwaggerEndpoint(
                                $"{officePath}/swagger/v1/swagger.json",
                                $"{ApiTitle} {DefaultApiVersion}");
                            swaggerUiOptions.RoutePrefix =
                                $"{officePath.TrimStart(Constants.CharArrays.ForwardSlash)}/swagger";

                            swaggerUiOptions.OAuthClientId(New.Cms.Core.Constants.OauthClientIds.Swagger);
                            swaggerUiOptions.OAuthUsePkce();
                        });
                    }
                },
                applicationBuilder =>
                {
                    IServiceProvider provider = applicationBuilder.ApplicationServices;

                    applicationBuilder.UseEndpoints(endpoints =>
                    {
                        GlobalSettings? settings = provider.GetRequiredService<IOptions<GlobalSettings>>().Value;
                        IHostingEnvironment hostingEnvironment = provider.GetRequiredService<IHostingEnvironment>();
                        var officePath = settings.GetBackOfficePath(hostingEnvironment);
                        // Maps attribute routed controllers.
                        endpoints.MapControllers();

                        // Serve contract
                        endpoints.MapGet($"{officePath}/management/api/openapi.json", async context =>
                        {
                            await context.Response.SendFileAsync(
                                new EmbeddedFileProvider(GetType().Assembly).GetFileInfo("OpenApi.json"));
                        });
                    });
                }));
        });
    }
}

