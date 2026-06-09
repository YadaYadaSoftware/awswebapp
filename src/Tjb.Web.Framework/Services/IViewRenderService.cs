namespace Tjb.Web.Services
{
    public interface IViewRenderService
    {
        Task<string> RenderToStringAsync<TModel>(string viewName, TModel model, CancellationToken cancellationToken = default);
    }
}
