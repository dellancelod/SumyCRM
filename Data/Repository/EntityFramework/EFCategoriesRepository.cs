using Microsoft.EntityFrameworkCore;
using SumyCRM.Data.Repository.Interfaces;
using SumyCRM.Models;

namespace SumyCRM.Data.Repository.EntityFramework
{
    public class EFCategoriesRepository : ICategoriesRepository
    {

        private readonly AppDbContext context;
        private readonly IWebHostEnvironment hostEnvironment;
        public EFCategoriesRepository(AppDbContext context, IWebHostEnvironment hostEnvironment)
        {
            this.context = context;
            this.hostEnvironment = hostEnvironment;
        }

        public async Task DeleteCategoryAsync(Guid id)
        {
            var category = await context.Categories.FirstOrDefaultAsync(x => x.Id == id);
           

            context.Categories.Remove(category);
            await context.SaveChangesAsync();
        }

        public async Task DeleteCategoryAsync(Category category)
        {
            context.Categories.Remove(category);
            await context.SaveChangesAsync();
        }

        public IQueryable<Category> GetCategories()
        {
            return context.Categories;
        }

        public async Task<Category> GetCategoryByIdAsync(Guid id)
        {
            return await context.Categories.FirstOrDefaultAsync(x => x.Id == id);
        }

        public async Task<IList<Request>> GetRequestsInCategoryAsync(Guid id)
        {
            return await context.Requests
                .Where(x => x.CategoryId == id).ToListAsync();
        }

        public bool IsUniqueCategoryTitle(string title, Guid? id = null)
        {
            return !context.Categories
                .Where(s => s.Title == title && s.Id != id)
                .Any();
        }

        public async Task SaveCategoryAsync(Category entity)
        {
            if (entity.Id == default)
            {
                context.Entry(entity).State = EntityState.Added;
            }
            else
            {
                context.Entry(entity).State = EntityState.Modified;
            }
            await context.SaveChangesAsync();
        }
    }
}
