using Drilling.Common.Alarm;
using Drilling.Common.Interface;
using Drilling.Common.InterLock;
using Drilling.Common.Managers;
using Drilling.Common.Motion;
using Drilling.Common.Station;

namespace Drilling.Common.Managers;

public enum EN_RECIPE_DATA_TYPE
{
    String,
    Int,
    Double,
    Bool
}

public sealed record ST_RECIPE_PARAM(
    string Name,
    string Value,
    string Unit,
    string Range,
    string DefaultValue,
    string Tab = "",
    string Group = "",
    string Key = "",
    string Description = "",
    bool Show = true,
    bool Use = true,
    int DisplayOrder = 0,
    EN_RECIPE_DATA_TYPE DataType = EN_RECIPE_DATA_TYPE.String,
    double ChangeLimit = 0.0,
    double Min = 0.0,
    double Max = 0.0,
    IReadOnlyDictionary<string, string>? Extra = null);

public sealed record ST_RECIPE_HISTORY(
    DateTimeOffset ChangedAt,
    string ItemName,
    string OldValue,
    string NewValue,
    string OperatorId,
    string RecipeName = "",
    string Action = "",
    string Tab = "",
    string Group = "");

public sealed record ST_RECIPE_DATA(
    string Id,
    string Name,
    IReadOnlyList<ST_RECIPE_PARAM> Parameters,
    IReadOnlyList<ST_RECIPE_HISTORY> History);

public sealed record ST_RECIPE_FORM_ITEM(
    string Tab,
    string Group,
    string Name,
    string DisplayName,
    string CimName,
    EN_RECIPE_DATA_TYPE DataType,
    string Unit,
    bool Show,
    bool Use,
    string DefaultValue,
    double Scale,
    double ChangeLimit,
    double Min,
    double Max,
    string Description,
    int DisplayOrder,
    IReadOnlyDictionary<string, string>? Extra = null);

public sealed record ST_RECIPE_VALUE(
    string Tab,
    string Name,
    string Value,
    IReadOnlyList<string>? Extra = null);

public interface IRecipeFile
{
    Task<IReadOnlyList<ST_RECIPE_DATA>> LoadAll(CancellationToken cancellationToken = default);

    Task<ST_RECIPE_DATA?> Find(string recipeId, CancellationToken cancellationToken = default);

    Task Save(ST_RECIPE_DATA recipe, CancellationToken cancellationToken = default);

    Task Rename(
        string oldRecipeId,
        string newRecipeId,
        CancellationToken cancellationToken = default);

    Task Delete(string recipeId, CancellationToken cancellationToken = default);
}
public interface IRecipeManager
{
    Task<IReadOnlyList<ST_RECIPE_DATA>> LoadRecipes(CancellationToken cancellationToken = default);

    Task SaveRecipe(ST_RECIPE_DATA recipe, CancellationToken cancellationToken = default);

    Task RenameRecipe(
        string oldRecipeId,
        string newRecipeId,
        CancellationToken cancellationToken = default);

    Task DeleteRecipe(string recipeId, CancellationToken cancellationToken = default);
}

public sealed class CRecipeManager(IRecipeFile recipeFile) : IRecipeManager
{
    public Task<IReadOnlyList<ST_RECIPE_DATA>> LoadRecipes(CancellationToken cancellationToken = default)
    {
        return recipeFile.LoadAll(cancellationToken);
    }

    public Task SaveRecipe(ST_RECIPE_DATA recipe, CancellationToken cancellationToken = default)
    {
        return recipeFile.Save(recipe, cancellationToken);
    }

    public Task RenameRecipe(
        string oldRecipeId,
        string newRecipeId,
        CancellationToken cancellationToken = default)
    {
        return recipeFile.Rename(oldRecipeId, newRecipeId, cancellationToken);
    }

    public Task DeleteRecipe(string recipeId, CancellationToken cancellationToken = default)
    {
        return recipeFile.Delete(recipeId, cancellationToken);
    }
}

