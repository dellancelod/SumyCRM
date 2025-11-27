using SumyCRM.Models;

namespace SumyCRM.Data.Repository.Interfaces
{
    public interface ICategoriesRepository
    {
        IQueryable<Category> GetCategories();
        Task<Category> GetCategoryByIdAsync(Guid id);
        Task<IList<Request>> GetRequestsInCategoryAsync(Guid id);
        Task SaveCategoryAsync(Category entity);
        Task DeleteCategoryAsync(Guid id);
        Task DeleteCategoryAsync(Category category);
        bool IsUniqueCategoryTitle(string title, Guid? id = null);
    }
}
