using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.AspNetCore.Mvc.Razor;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.AspNetCore.Routing;

namespace Tjb.Web.Services
{
    public class ViewRenderService : IViewRenderService
    {
        private readonly IRazorViewEngine _razorViewEngine;
        private readonly ITempDataProvider _tempDataProvider;
        private readonly IServiceProvider _serviceProvider;
        private readonly IHttpContextAccessor _httpContextAccessor;

        public ViewRenderService(
            IRazorViewEngine razorViewEngine,
            ITempDataProvider tempDataProvider,
            IServiceProvider serviceProvider,
            IHttpContextAccessor httpContextAccessor)
        {
            _razorViewEngine = razorViewEngine;
            _tempDataProvider = tempDataProvider;
            _serviceProvider = serviceProvider;
            _httpContextAccessor = httpContextAccessor;
        }

        public async Task<string> RenderToStringAsync<TModel>(string viewName, TModel model, CancellationToken cancellationToken = default)
        {
            var httpContext = _httpContextAccessor.HttpContext ?? new DefaultHttpContext { RequestServices = _serviceProvider };
            var actionContext = new ActionContext(httpContext, httpContext.GetRouteData() ?? new RouteData(), new ActionDescriptor());

            var viewResult = _razorViewEngine.GetView(executingFilePath: null, viewPath: viewName, isMainPage: false);
            if (!viewResult.Success)
            {
                viewResult = _razorViewEngine.FindView(actionContext, viewName, isMainPage: false);
            }

            if (!viewResult.Success || viewResult.View == null)
            {
                var searched = viewResult.SearchedLocations != null ? string.Join("; ", viewResult.SearchedLocations) : "(none)";
                throw new InvalidOperationException($"Email template '{viewName}' was not found. Searched: {searched}");
            }

            await using var writer = new StringWriter();
            var viewDictionary = new ViewDataDictionary<TModel>(new EmptyModelMetadataProvider(), new ModelStateDictionary())
            {
                Model = model
            };

            var viewContext = new ViewContext(
                actionContext,
                viewResult.View,
                viewDictionary,
                new TempDataDictionary(httpContext, _tempDataProvider),
                writer,
                new HtmlHelperOptions());

            await viewResult.View.RenderAsync(viewContext);
            return writer.ToString();
        }
    }
}
