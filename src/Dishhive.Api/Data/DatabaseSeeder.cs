using Dishhive.Api.Models;
using Microsoft.EntityFrameworkCore;

namespace Dishhive.Api.Data;

/// <summary>
/// Seeds the database with sample data for development environment.
/// </summary>
public static class DatabaseSeeder
{
    public static async Task SeedAsync(DishhiveDbContext context)
    {
        // Only seed if database is empty
        if (await context.FamilyMembers.AnyAsync())
        {
            return;
        }

        // Create family members
        var tom = new FamilyMember { Name = "Tom" };
        var jane = new FamilyMember { Name = "Jane" };
        var sally = new FamilyMember { Name = "Sally" };

        context.FamilyMembers.AddRange(tom, jane, sally);
        await context.SaveChangesAsync();

        // Create recipes with ingredients and steps
        var recipes = CreateRecipes();
        context.Recipes.AddRange(recipes);
        await context.SaveChangesAsync();
    }

    private static List<Recipe> CreateRecipes()
    {
        return new List<Recipe>
        {
            new Recipe
            {
                Title = "Penne with spinach, ricotta, pesto and bacon",
                Servings = 3,
                PrepTimeMinutes = 10,
                CookTimeMinutes = 20,
                Tags = new List<string> { "pasta", "Italian" },
                Ingredients = new List<RecipeIngredient>
                {
                    new() { Name = "penne pasta", Quantity = 400, Unit = "g", SortOrder = 1 },
                    new() { Name = "fresh spinach", Quantity = 200, Unit = "g", SortOrder = 2 },
                    new() { Name = "ricotta cheese", Quantity = 250, Unit = "g", SortOrder = 3 },
                    new() { Name = "pesto", Quantity = 100, Unit = "g", SortOrder = 4 },
                    new() { Name = "bacon strips", Quantity = 150, Unit = "g", SortOrder = 5 },
                    new() { Name = "garlic cloves", Quantity = 2, Unit = "pieces", SortOrder = 6 },
                    new() { Name = "olive oil", Quantity = 2, Unit = "tbsp", SortOrder = 7 },
                    new() { Name = "salt", SortOrder = 8 },
                    new() { Name = "black pepper", SortOrder = 9 }
                },
                Steps = new List<RecipeStep>
                {
                    new() { StepNumber = 1, Instruction = "Cook penne according to package directions in salted boiling water. Drain and set aside." },
                    new() { StepNumber = 2, Instruction = "Fry bacon strips in a large pan over medium heat until crispy. Remove and chop." },
                    new() { StepNumber = 3, Instruction = "In the same pan, sauté fresh spinach with minced garlic and olive oil for 2-3 minutes." },
                    new() { StepNumber = 4, Instruction = "Mix ricotta and pesto in a bowl. Season with salt and pepper." },
                    new() { StepNumber = 5, Instruction = "Combine hot pasta with spinach mixture and ricotta-pesto sauce. Toss gently." },
                    new() { StepNumber = 6, Instruction = "Top with crispy bacon and serve immediately." }
                }
            },
            new Recipe
            {
                Title = "Farfalle with broccoli, cream cheese and Italian ham",
                Servings = 3,
                PrepTimeMinutes = 10,
                CookTimeMinutes = 15,
                Tags = new List<string> { "pasta", "Italian" },
                Ingredients = new List<RecipeIngredient>
                {
                    new() { Name = "farfalle pasta", Quantity = 400, Unit = "g", SortOrder = 1 },
                    new() { Name = "broccoli florets", Quantity = 300, Unit = "g", SortOrder = 2 },
                    new() { Name = "Philadelphia cheese", Quantity = 200, Unit = "g", SortOrder = 3 },
                    new() { Name = "Italian ham", Quantity = 150, Unit = "g", SortOrder = 4 },
                    new() { Name = "onion", Quantity = 1, Unit = "piece", SortOrder = 5 },
                    new() { Name = "garlic cloves", Quantity = 2, Unit = "pieces", SortOrder = 6 },
                    new() { Name = "olive oil", Quantity = 2, Unit = "tbsp", SortOrder = 7 },
                    new() { Name = "pasta water", Quantity = 100, Unit = "ml", SortOrder = 8 },
                    new() { Name = "salt and pepper", SortOrder = 9 }
                },
                Steps = new List<RecipeStep>
                {
                    new() { StepNumber = 1, Instruction = "Cook farfalle in salted boiling water. Add broccoli in the last 4 minutes. Reserve 1 cup pasta water." },
                    new() { StepNumber = 2, Instruction = "Chop Italian ham into small pieces." },
                    new() { StepNumber = 3, Instruction = "Sauté diced onion and garlic in olive oil in a large pan until softened." },
                    new() { StepNumber = 4, Instruction = "Add chopped ham and cook for 2 minutes." },
                    new() { StepNumber = 5, Instruction = "Reduce heat and stir in Philadelphia cheese until melted, adding pasta water as needed." },
                    new() { StepNumber = 6, Instruction = "Add drained pasta and broccoli. Toss well. Season with salt and pepper." }
                }
            },
            new Recipe
            {
                Title = "Enchiladas with tomato sauce, kidney beans and corn",
                Servings = 3,
                PrepTimeMinutes = 20,
                CookTimeMinutes = 25,
                Tags = new List<string> { "Mexican", "vegetarian" },
                Ingredients = new List<RecipeIngredient>
                {
                    new() { Name = "corn tortillas", Quantity = 8, Unit = "pieces", SortOrder = 1 },
                    new() { Name = "canned kidney beans", Quantity = 400, Unit = "g", SortOrder = 2 },
                    new() { Name = "corn kernels", Quantity = 250, Unit = "g", SortOrder = 3 },
                    new() { Name = "tomato sauce", Quantity = 500, Unit = "ml", SortOrder = 4 },
                    new() { Name = "cheddar cheese", Quantity = 150, Unit = "g", SortOrder = 5 },
                    new() { Name = "sour cream", Quantity = 100, Unit = "g", SortOrder = 6 },
                    new() { Name = "guacamole", Quantity = 150, Unit = "g", SortOrder = 7 },
                    new() { Name = "onion", Quantity = 1, Unit = "piece", SortOrder = 8 },
                    new() { Name = "garlic clove", Quantity = 2, Unit = "pieces", SortOrder = 9 },
                    new() { Name = "cumin", Quantity = 1, Unit = "tsp", SortOrder = 10 },
                    new() { Name = "chili powder", Quantity = 1, Unit = "tsp", SortOrder = 11 },
                    new() { Name = "salt and pepper", SortOrder = 12 }
                },
                Steps = new List<RecipeStep>
                {
                    new() { StepNumber = 1, Instruction = "Preheat oven to 180°C (350°F)." },
                    new() { StepNumber = 2, Instruction = "Heat tomato sauce with sautéed onion, garlic, cumin, and chili powder. Season with salt and pepper." },
                    new() { StepNumber = 3, Instruction = "Mix kidney beans, corn, and 1/3 of the cheese in a bowl." },
                    new() { StepNumber = 4, Instruction = "Warm tortillas to make them pliable." },
                    new() { StepNumber = 5, Instruction = "Dip each tortilla in sauce, fill with bean mixture, roll, and place seam-side down in a greased baking dish." },
                    new() { StepNumber = 6, Instruction = "Pour remaining sauce over enchiladas. Top with remaining cheese." },
                    new() { StepNumber = 7, Instruction = "Bake for 25 minutes until bubbling. Serve with sour cream and guacamole." }
                }
            },
            new Recipe
            {
                Title = "Spaghetti bolognese",
                Servings = 3,
                PrepTimeMinutes = 10,
                CookTimeMinutes = 30,
                Tags = new List<string> { "pasta", "Italian" },
                Ingredients = new List<RecipeIngredient>
                {
                    new() { Name = "spaghetti pasta", Quantity = 400, Unit = "g", SortOrder = 1 },
                    new() { Name = "ground beef", Quantity = 500, Unit = "g", SortOrder = 2 },
                    new() { Name = "canned tomatoes", Quantity = 400, Unit = "g", SortOrder = 3 },
                    new() { Name = "tomato puree", Quantity = 2, Unit = "tbsp", SortOrder = 4 },
                    new() { Name = "onion", Quantity = 1, Unit = "piece", SortOrder = 5 },
                    new() { Name = "garlic cloves", Quantity = 2, Unit = "pieces", SortOrder = 6 },
                    new() { Name = "olive oil", Quantity = 2, Unit = "tbsp", SortOrder = 7 },
                    new() { Name = "red wine", Quantity = 100, Unit = "ml", SortOrder = 8 },
                    new() { Name = "beef broth", Quantity = 200, Unit = "ml", SortOrder = 9 },
                    new() { Name = "parmesan cheese", Quantity = 50, Unit = "g", SortOrder = 10 },
                    new() { Name = "salt and pepper", SortOrder = 11 }
                },
                Steps = new List<RecipeStep>
                {
                    new() { StepNumber = 1, Instruction = "Cook spaghetti according to package directions in salted boiling water." },
                    new() { StepNumber = 2, Instruction = "Sauté finely chopped onion and garlic in olive oil until golden." },
                    new() { StepNumber = 3, Instruction = "Add ground beef and cook until browned, breaking it up as it cooks." },
                    new() { StepNumber = 4, Instruction = "Stir in tomato puree and cook for 1 minute." },
                    new() { StepNumber = 5, Instruction = "Deglaze with red wine, then add canned tomatoes and beef broth." },
                    new() { StepNumber = 6, Instruction = "Simmer for 20-25 minutes, stirring occasionally. Season with salt and pepper." },
                    new() { StepNumber = 7, Instruction = "Serve sauce over drained pasta and top with grated parmesan." }
                }
            },
            new Recipe
            {
                Title = "Lasagne with courgette, carrots and bolognese sauce",
                Servings = 6,
                PrepTimeMinutes = 30,
                CookTimeMinutes = 45,
                Tags = new List<string> { "Italian", "oven", "vegetable" },
                Ingredients = new List<RecipeIngredient>
                {
                    new() { Name = "lasagne sheets", Quantity = 12, Unit = "pieces", SortOrder = 1 },
                    new() { Name = "ground beef", Quantity = 500, Unit = "g", SortOrder = 2 },
                    new() { Name = "zucchini", Quantity = 2, Unit = "pieces", SortOrder = 3 },
                    new() { Name = "carrots", Quantity = 2, Unit = "pieces", SortOrder = 4 },
                    new() { Name = "canned tomatoes", Quantity = 400, Unit = "g", SortOrder = 5 },
                    new() { Name = "tomato puree", Quantity = 3, Unit = "tbsp", SortOrder = 6 },
                    new() { Name = "ricotta cheese", Quantity = 250, Unit = "g", SortOrder = 7 },
                    new() { Name = "mozzarella cheese", Quantity = 200, Unit = "g", SortOrder = 8 },
                    new() { Name = "parmesan cheese", Quantity = 100, Unit = "g", SortOrder = 9 },
                    new() { Name = "egg", Quantity = 1, Unit = "piece", SortOrder = 10 },
                    new() { Name = "onion", Quantity = 1, Unit = "piece", SortOrder = 11 },
                    new() { Name = "garlic cloves", Quantity = 2, Unit = "pieces", SortOrder = 12 },
                    new() { Name = "olive oil", Quantity = 3, Unit = "tbsp", SortOrder = 13 },
                    new() { Name = "salt and pepper", SortOrder = 14 }
                },
                Steps = new List<RecipeStep>
                {
                    new() { StepNumber = 1, Instruction = "Preheat oven to 180°C (350°F)." },
                    new() { StepNumber = 2, Instruction = "Dice zucchini and carrots. Sauté with chopped onion and garlic in olive oil." },
                    new() { StepNumber = 3, Instruction = "Add ground beef and cook until browned. Stir in tomato puree." },
                    new() { StepNumber = 4, Instruction = "Add canned tomatoes and simmer for 15 minutes. Season with salt and pepper." },
                    new() { StepNumber = 5, Instruction = "Mix ricotta, egg, half the mozzarella, and half the parmesan in a bowl." },
                    new() { StepNumber = 6, Instruction = "Spread a thin layer of bolognese sauce on the bottom of a 9x13\" baking dish." },
                    new() { StepNumber = 7, Instruction = "Layer: lasagne sheet, ricotta mixture, bolognese sauce. Repeat until all ingredients used." },
                    new() { StepNumber = 8, Instruction = "Top with remaining mozzarella and parmesan." },
                    new() { StepNumber = 9, Instruction = "Bake for 45 minutes until bubbly and golden. Let rest 10 minutes before serving." }
                }
            },
            new Recipe
            {
                Title = "Green beans, pasta and goat cheese with bacon",
                Servings = 3,
                PrepTimeMinutes = 10,
                CookTimeMinutes = 15,
                Tags = new List<string> { "pasta", "vegetable" },
                Ingredients = new List<RecipeIngredient>
                {
                    new() { Name = "pasta", Quantity = 400, Unit = "g", SortOrder = 1 },
                    new() { Name = "green beans", Quantity = 300, Unit = "g", SortOrder = 2 },
                    new() { Name = "goat cheese", Quantity = 150, Unit = "g", SortOrder = 3 },
                    new() { Name = "bacon strips", Quantity = 150, Unit = "g", SortOrder = 4 },
                    new() { Name = "garlic cloves", Quantity = 2, Unit = "pieces", SortOrder = 5 },
                    new() { Name = "olive oil", Quantity = 3, Unit = "tbsp", SortOrder = 6 },
                    new() { Name = "lemon juice", Quantity = 2, Unit = "tbsp", SortOrder = 7 },
                    new() { Name = "salt and pepper", SortOrder = 8 }
                },
                Steps = new List<RecipeStep>
                {
                    new() { StepNumber = 1, Instruction = "Cook pasta in salted boiling water. Add green beans in the last 4 minutes. Drain." },
                    new() { StepNumber = 2, Instruction = "Fry bacon until crispy. Chop and set aside." },
                    new() { StepNumber = 3, Instruction = "In the bacon fat, sauté minced garlic for 30 seconds." },
                    new() { StepNumber = 4, Instruction = "Return bacon to pan. Add cooked pasta and beans." },
                    new() { StepNumber = 5, Instruction = "Toss with olive oil and lemon juice. Season with salt and pepper." },
                    new() { StepNumber = 6, Instruction = "Crumble goat cheese over top and serve immediately." }
                }
            },
            new Recipe
            {
                Title = "Mexican-style minced meat with tortillas, salsa and tomato",
                Servings = 3,
                PrepTimeMinutes = 15,
                CookTimeMinutes = 15,
                Tags = new List<string> { "Mexican", "quick" },
                Ingredients = new List<RecipeIngredient>
                {
                    new() { Name = "ground beef", Quantity = 500, Unit = "g", SortOrder = 1 },
                    new() { Name = "flour tortillas", Quantity = 8, Unit = "pieces", SortOrder = 2 },
                    new() { Name = "salsa", Quantity = 200, Unit = "g", SortOrder = 3 },
                    new() { Name = "tomatoes", Quantity = 2, Unit = "pieces", SortOrder = 4 },
                    new() { Name = "onion", Quantity = 1, Unit = "piece", SortOrder = 5 },
                    new() { Name = "garlic cloves", Quantity = 2, Unit = "pieces", SortOrder = 6 },
                    new() { Name = "cumin", Quantity = 1, Unit = "tsp", SortOrder = 7 },
                    new() { Name = "chili powder", Quantity = 1, Unit = "tsp", SortOrder = 8 },
                    new() { Name = "olive oil", Quantity = 2, Unit = "tbsp", SortOrder = 9 },
                    new() { Name = "cilantro", Quantity = 10, Unit = "g", SortOrder = 10 },
                    new() { Name = "salt and pepper", SortOrder = 11 }
                },
                Steps = new List<RecipeStep>
                {
                    new() { StepNumber = 1, Instruction = "Sauté diced onion and garlic in olive oil until softened." },
                    new() { StepNumber = 2, Instruction = "Add ground beef and cook until browned, breaking it up as it cooks." },
                    new() { StepNumber = 3, Instruction = "Stir in cumin, chili powder, and salsa. Simmer for 5-10 minutes." },
                    new() { StepNumber = 4, Instruction = "Dice tomatoes. Season meat mixture with salt and pepper." },
                    new() { StepNumber = 5, Instruction = "Warm tortillas. Fill each with seasoned meat, diced tomato, and cilantro." },
                    new() { StepNumber = 6, Instruction = "Serve with extra salsa on the side." }
                }
            },
            new Recipe
            {
                Title = "Fish sticks with mashed potato and peas",
                Servings = 3,
                PrepTimeMinutes = 10,
                CookTimeMinutes = 20,
                Tags = new List<string> { "fish", "quick", "comfort-food" },
                Ingredients = new List<RecipeIngredient>
                {
                    new() { Name = "frozen fish sticks", Quantity = 12, Unit = "pieces", SortOrder = 1 },
                    new() { Name = "potatoes", Quantity = 800, Unit = "g", SortOrder = 2 },
                    new() { Name = "frozen peas", Quantity = 200, Unit = "g", SortOrder = 3 },
                    new() { Name = "butter", Quantity = 50, Unit = "g", SortOrder = 4 },
                    new() { Name = "milk", Quantity = 100, Unit = "ml", SortOrder = 5 },
                    new() { Name = "salt and pepper", SortOrder = 6 },
                    new() { Name = "lemon", Quantity = 1, Unit = "piece", SortOrder = 7 }
                },
                Steps = new List<RecipeStep>
                {
                    new() { StepNumber = 1, Instruction = "Peel and chop potatoes. Cook in salted boiling water for 15-20 minutes until tender." },
                    new() { StepNumber = 2, Instruction = "Bake fish sticks according to package directions." },
                    new() { StepNumber = 3, Instruction = "Heat peas in a small pot or microwave until warm." },
                    new() { StepNumber = 4, Instruction = "Drain potatoes and mash with butter and milk. Season with salt and pepper." },
                    new() { StepNumber = 5, Instruction = "Arrange fish sticks, mashed potatoes, and peas on plates." },
                    new() { StepNumber = 6, Instruction = "Serve with lemon wedges." }
                }
            },
            new Recipe
            {
                Title = "Quiche with bacon, goat cheese and spinach",
                Servings = 6,
                PrepTimeMinutes = 20,
                CookTimeMinutes = 40,
                Tags = new List<string> { "quiche", "vegetable", "oven" },
                Ingredients = new List<RecipeIngredient>
                {
                    new() { Name = "pie crust", Quantity = 1, Unit = "piece", SortOrder = 1 },
                    new() { Name = "eggs", Quantity = 4, Unit = "pieces", SortOrder = 2 },
                    new() { Name = "heavy cream", Quantity = 200, Unit = "ml", SortOrder = 3 },
                    new() { Name = "fresh spinach", Quantity = 200, Unit = "g", SortOrder = 4 },
                    new() { Name = "goat cheese", Quantity = 150, Unit = "g", SortOrder = 5 },
                    new() { Name = "bacon strips", Quantity = 150, Unit = "g", SortOrder = 6 },
                    new() { Name = "onion", Quantity = 1, Unit = "piece", SortOrder = 7 },
                    new() { Name = "garlic clove", Quantity = 1, Unit = "piece", SortOrder = 8 },
                    new() { Name = "salt and pepper", SortOrder = 9 },
                    new() { Name = "nutmeg", SortOrder = 10 }
                },
                Steps = new List<RecipeStep>
                {
                    new() { StepNumber = 1, Instruction = "Preheat oven to 200°C (400°F)." },
                    new() { StepNumber = 2, Instruction = "Place pie crust in a 9-inch tart pan." },
                    new() { StepNumber = 3, Instruction = "Fry diced bacon until crispy. Remove and chop." },
                    new() { StepNumber = 4, Instruction = "Sauté spinach with diced onion and garlic. Drain any excess moisture." },
                    new() { StepNumber = 5, Instruction = "Whisk eggs with heavy cream. Season with salt, pepper, and a pinch of nutmeg." },
                    new() { StepNumber = 6, Instruction = "Distribute spinach, bacon, and goat cheese in the crust." },
                    new() { StepNumber = 7, Instruction = "Pour egg mixture over filling." },
                    new() { StepNumber = 8, Instruction = "Bake for 35-40 minutes until set and lightly golden. Let cool 5 minutes before serving." }
                }
            },
            new Recipe
            {
                Title = "Veal escalopes with fresh tomato sauce and mozzarella with tagliatelle",
                Servings = 3,
                PrepTimeMinutes = 15,
                CookTimeMinutes = 25,
                Tags = new List<string> { "veal", "Italian", "oven" },
                Ingredients = new List<RecipeIngredient>
                {
                    new() { Name = "veal cutlets", Quantity = 4, Unit = "pieces", SortOrder = 1 },
                    new() { Name = "tagliatelle pasta", Quantity = 400, Unit = "g", SortOrder = 2 },
                    new() { Name = "fresh tomatoes", Quantity = 500, Unit = "g", SortOrder = 3 },
                    new() { Name = "mozzarella cheese", Quantity = 150, Unit = "g", SortOrder = 4 },
                    new() { Name = "garlic cloves", Quantity = 3, Unit = "pieces", SortOrder = 5 },
                    new() { Name = "basil", Quantity = 10, Unit = "g", SortOrder = 6 },
                    new() { Name = "olive oil", Quantity = 4, Unit = "tbsp", SortOrder = 7 },
                    new() { Name = "flour", Quantity = 2, Unit = "tbsp", SortOrder = 8 },
                    new() { Name = "salt and pepper", SortOrder = 9 }
                },
                Steps = new List<RecipeStep>
                {
                    new() { StepNumber = 1, Instruction = "Preheat oven to 200°C (400°F)." },
                    new() { StepNumber = 2, Instruction = "Cook tagliatelle according to package directions. Drain and set aside." },
                    new() { StepNumber = 3, Instruction = "Blanch tomatoes briefly, then peel and chop. Sauté with minced garlic and olive oil for 5 minutes. Add torn basil." },
                    new() { StepNumber = 4, Instruction = "Lightly flour veal cutlets. Sauté in olive oil for 2-3 minutes per side. Season with salt and pepper." },
                    new() { StepNumber = 5, Instruction = "Place veal in a baking dish. Top each cutlet with fresh tomato sauce and slices of mozzarella." },
                    new() { StepNumber = 6, Instruction = "Bake for 12-15 minutes until cheese is melted and bubbly." },
                    new() { StepNumber = 7, Instruction = "Serve veal over tagliatelle pasta." }
                }
            },
            new Recipe
            {
                Title = "Homemade pizza",
                Servings = 3,
                PrepTimeMinutes = 30,
                CookTimeMinutes = 20,
                Tags = new List<string> { "pizza", "Italian", "oven" },
                Ingredients = new List<RecipeIngredient>
                {
                    new() { Name = "pizza dough", Quantity = 500, Unit = "g", SortOrder = 1 },
                    new() { Name = "tomato sauce", Quantity = 200, Unit = "ml", SortOrder = 2 },
                    new() { Name = "mozzarella cheese", Quantity = 250, Unit = "g", SortOrder = 3 },
                    new() { Name = "fresh basil", Quantity = 10, Unit = "g", SortOrder = 4 },
                    new() { Name = "pepperoni slices", Quantity = 100, Unit = "g", SortOrder = 5 },
                    new() { Name = "bell peppers", Quantity = 1, Unit = "piece", SortOrder = 6 },
                    new() { Name = "onion", Quantity = 1, Unit = "piece", SortOrder = 7 },
                    new() { Name = "mushrooms", Quantity = 150, Unit = "g", SortOrder = 8 },
                    new() { Name = "olive oil", Quantity = 2, Unit = "tbsp", SortOrder = 9 },
                    new() { Name = "salt and oregano", SortOrder = 10 }
                },
                Steps = new List<RecipeStep>
                {
                    new() { StepNumber = 1, Instruction = "Preheat oven to 220°C (425°F)." },
                    new() { StepNumber = 2, Instruction = "Roll or stretch pizza dough to fit a 12-inch pizza pan." },
                    new() { StepNumber = 3, Instruction = "Spread tomato sauce evenly over dough. Season with salt and oregano." },
                    new() { StepNumber = 4, Instruction = "Slice bell peppers, onion, and mushrooms. Distribute over sauce." },
                    new() { StepNumber = 5, Instruction = "Top with pepperoni slices and mozzarella cheese." },
                    new() { StepNumber = 6, Instruction = "Drizzle with olive oil." },
                    new() { StepNumber = 7, Instruction = "Bake for 18-20 minutes until crust is golden and cheese is bubbly." },
                    new() { StepNumber = 8, Instruction = "Top with fresh basil and serve hot." }
                }
            },
            new Recipe
            {
                Title = "Wok with steak strips, green pepper, red onion, sesame seeds and noodles",
                Servings = 3,
                PrepTimeMinutes = 15,
                CookTimeMinutes = 15,
                Tags = new List<string> { "wok", "Asian", "quick" },
                Ingredients = new List<RecipeIngredient>
                {
                    new() { Name = "beef steak strips", Quantity = 500, Unit = "g", SortOrder = 1 },
                    new() { Name = "egg noodles", Quantity = 300, Unit = "g", SortOrder = 2 },
                    new() { Name = "green bell pepper", Quantity = 2, Unit = "pieces", SortOrder = 3 },
                    new() { Name = "red onion", Quantity = 1, Unit = "piece", SortOrder = 4 },
                    new() { Name = "soy sauce", Quantity = 3, Unit = "tbsp", SortOrder = 5 },
                    new() { Name = "sesame oil", Quantity = 2, Unit = "tbsp", SortOrder = 6 },
                    new() { Name = "garlic cloves", Quantity = 2, Unit = "pieces", SortOrder = 7 },
                    new() { Name = "ginger", Quantity = 1, Unit = "tbsp", SortOrder = 8 },
                    new() { Name = "sesame seeds", Quantity = 2, Unit = "tbsp", SortOrder = 9 },
                    new() { Name = "vegetable oil", Quantity = 2, Unit = "tbsp", SortOrder = 10 }
                },
                Steps = new List<RecipeStep>
                {
                    new() { StepNumber = 1, Instruction = "Cook noodles according to package directions. Drain and set aside." },
                    new() { StepNumber = 2, Instruction = "Slice peppers and red onion into strips." },
                    new() { StepNumber = 3, Instruction = "Heat vegetable oil in a wok over high heat." },
                    new() { StepNumber = 4, Instruction = "Stir-fry beef strips in batches until cooked. Remove and set aside." },
                    new() { StepNumber = 5, Instruction = "Stir-fry peppers and onion for 3-4 minutes until tender-crisp." },
                    new() { StepNumber = 6, Instruction = "Add minced garlic and ginger, cook for 1 minute." },
                    new() { StepNumber = 7, Instruction = "Return beef to wok. Add cooked noodles, soy sauce, and sesame oil. Toss well." },
                    new() { StepNumber = 8, Instruction = "Top with sesame seeds and serve immediately." }
                }
            },
            new Recipe
            {
                Title = "Chicken wok with yellow pepper, apple, curry and coconut milk with rice",
                Servings = 3,
                PrepTimeMinutes = 15,
                CookTimeMinutes = 20,
                Tags = new List<string> { "wok", "Asian", "quick", "curry" },
                Ingredients = new List<RecipeIngredient>
                {
                    new() { Name = "chicken breast", Quantity = 500, Unit = "g", SortOrder = 1 },
                    new() { Name = "white rice", Quantity = 300, Unit = "g", SortOrder = 2 },
                    new() { Name = "yellow bell pepper", Quantity = 2, Unit = "pieces", SortOrder = 3 },
                    new() { Name = "apples", Quantity = 2, Unit = "pieces", SortOrder = 4 },
                    new() { Name = "coconut milk", Quantity = 400, Unit = "ml", SortOrder = 5 },
                    new() { Name = "curry paste", Quantity = 2, Unit = "tbsp", SortOrder = 6 },
                    new() { Name = "onion", Quantity = 1, Unit = "piece", SortOrder = 7 },
                    new() { Name = "garlic cloves", Quantity = 2, Unit = "pieces", SortOrder = 8 },
                    new() { Name = "vegetable oil", Quantity = 2, Unit = "tbsp", SortOrder = 9 },
                    new() { Name = "salt and pepper", SortOrder = 10 }
                },
                Steps = new List<RecipeStep>
                {
                    new() { StepNumber = 1, Instruction = "Cook rice according to package directions." },
                    new() { StepNumber = 2, Instruction = "Dice chicken breast. Slice peppers and core and dice apples." },
                    new() { StepNumber = 3, Instruction = "Heat vegetable oil in a wok over medium heat." },
                    new() { StepNumber = 4, Instruction = "Stir-fry diced onion and garlic for 1 minute." },
                    new() { StepNumber = 5, Instruction = "Add curry paste and fry for 1 minute until fragrant." },
                    new() { StepNumber = 6, Instruction = "Add chicken pieces and cook for 5 minutes until mostly cooked." },
                    new() { StepNumber = 7, Instruction = "Stir in coconut milk, peppers, and apples. Simmer for 10 minutes." },
                    new() { StepNumber = 8, Instruction = "Season with salt and pepper. Serve over white rice." }
                }
            },
            new Recipe
            {
                Title = "Chicken wok with red pepper, baby corn, red onion and cashews with rice",
                Servings = 3,
                PrepTimeMinutes = 15,
                CookTimeMinutes = 15,
                Tags = new List<string> { "wok", "Asian", "quick" },
                Ingredients = new List<RecipeIngredient>
                {
                    new() { Name = "chicken breast", Quantity = 500, Unit = "g", SortOrder = 1 },
                    new() { Name = "white rice", Quantity = 300, Unit = "g", SortOrder = 2 },
                    new() { Name = "red bell pepper", Quantity = 2, Unit = "pieces", SortOrder = 3 },
                    new() { Name = "baby corn", Quantity = 200, Unit = "g", SortOrder = 4 },
                    new() { Name = "red onion", Quantity = 1, Unit = "piece", SortOrder = 5 },
                    new() { Name = "cashew nuts", Quantity = 150, Unit = "g", SortOrder = 6 },
                    new() { Name = "soy sauce", Quantity = 3, Unit = "tbsp", SortOrder = 7 },
                    new() { Name = "garlic cloves", Quantity = 2, Unit = "pieces", SortOrder = 8 },
                    new() { Name = "ginger", Quantity = 1, Unit = "tbsp", SortOrder = 9 },
                    new() { Name = "vegetable oil", Quantity = 2, Unit = "tbsp", SortOrder = 10 }
                },
                Steps = new List<RecipeStep>
                {
                    new() { StepNumber = 1, Instruction = "Cook rice according to package directions." },
                    new() { StepNumber = 2, Instruction = "Cube chicken breast. Slice peppers and onion." },
                    new() { StepNumber = 3, Instruction = "Heat vegetable oil in a wok over high heat." },
                    new() { StepNumber = 4, Instruction = "Stir-fry chicken cubes for 5-6 minutes until cooked. Remove and set aside." },
                    new() { StepNumber = 5, Instruction = "Stir-fry peppers, baby corn, and onion for 4 minutes." },
                    new() { StepNumber = 6, Instruction = "Return chicken to wok. Add minced garlic and ginger." },
                    new() { StepNumber = 7, Instruction = "Toss with soy sauce and top with cashew nuts." },
                    new() { StepNumber = 8, Instruction = "Serve over white rice." }
                }
            },
            new Recipe
            {
                Title = "Pita with feta",
                Servings = 3,
                PrepTimeMinutes = 10,
                CookTimeMinutes = 5,
                Tags = new List<string> { "Greek", "vegetarian", "quick" },
                Ingredients = new List<RecipeIngredient>
                {
                    new() { Name = "pita bread", Quantity = 4, Unit = "pieces", SortOrder = 1 },
                    new() { Name = "feta cheese", Quantity = 200, Unit = "g", SortOrder = 2 },
                    new() { Name = "cucumber", Quantity = 1, Unit = "piece", SortOrder = 3 },
                    new() { Name = "tomatoes", Quantity = 2, Unit = "pieces", SortOrder = 4 },
                    new() { Name = "red onion", Quantity = 1, Unit = "piece", SortOrder = 5 },
                    new() { Name = "kalamata olives", Quantity = 100, Unit = "g", SortOrder = 6 },
                    new() { Name = "fresh parsley", Quantity = 10, Unit = "g", SortOrder = 7 },
                    new() { Name = "olive oil", Quantity = 2, Unit = "tbsp", SortOrder = 8 },
                    new() { Name = "lemon juice", Quantity = 2, Unit = "tbsp", SortOrder = 9 },
                    new() { Name = "salt and pepper", SortOrder = 10 }
                },
                Steps = new List<RecipeStep>
                {
                    new() { StepNumber = 1, Instruction = "Dice cucumber, tomatoes, and red onion." },
                    new() { StepNumber = 2, Instruction = "Toss vegetables with olive oil, lemon juice, salt, and pepper." },
                    new() { StepNumber = 3, Instruction = "Warm pita bread briefly in a pan or oven." },
                    new() { StepNumber = 4, Instruction = "Crumble feta cheese and chop kalamata olives." },
                    new() { StepNumber = 5, Instruction = "Fill each pita with vegetable mixture. Top with feta and olives." },
                    new() { StepNumber = 6, Instruction = "Garnish with fresh parsley and serve." }
                }
            },
            new Recipe
            {
                Title = "Cheeseburgers with bacon",
                Servings = 3,
                PrepTimeMinutes = 15,
                CookTimeMinutes = 10,
                Tags = new List<string> { "burger", "quick" },
                Ingredients = new List<RecipeIngredient>
                {
                    new() { Name = "ground beef", Quantity = 600, Unit = "g", SortOrder = 1 },
                    new() { Name = "hamburger buns", Quantity = 4, Unit = "pieces", SortOrder = 2 },
                    new() { Name = "cheddar cheese", Quantity = 100, Unit = "g", SortOrder = 3 },
                    new() { Name = "bacon strips", Quantity = 8, Unit = "pieces", SortOrder = 4 },
                    new() { Name = "lettuce leaves", Quantity = 4, Unit = "pieces", SortOrder = 5 },
                    new() { Name = "tomato slices", Quantity = 8, Unit = "pieces", SortOrder = 6 },
                    new() { Name = "onion slices", Quantity = 4, Unit = "pieces", SortOrder = 7 },
                    new() { Name = "pickles", Quantity = 50, Unit = "g", SortOrder = 8 },
                    new() { Name = "ketchup", Quantity = 50, Unit = "g", SortOrder = 9 },
                    new() { Name = "mayonnaise", Quantity = 50, Unit = "g", SortOrder = 10 },
                    new() { Name = "salt and pepper", SortOrder = 11 }
                },
                Steps = new List<RecipeStep>
                {
                    new() { StepNumber = 1, Instruction = "Fry bacon strips until crispy. Drain on paper towels." },
                    new() { StepNumber = 2, Instruction = "Form ground beef into 4 patties. Season generously with salt and pepper." },
                    new() { StepNumber = 3, Instruction = "Cook patties on a hot griddle or skillet for 3-4 minutes per side." },
                    new() { StepNumber = 4, Instruction = "Top each burger with a slice of cheddar cheese in the last minute of cooking." },
                    new() { StepNumber = 5, Instruction = "Toast buns if desired." },
                    new() { StepNumber = 6, Instruction = "Build burgers: bottom bun, mayo, lettuce, burger, tomato, onion, bacon, pickles, ketchup, top bun." }
                }
            },
            new Recipe
            {
                Title = "Thai burgers",
                Servings = 3,
                PrepTimeMinutes = 15,
                CookTimeMinutes = 10,
                Tags = new List<string> { "burger", "Thai", "quick" },
                Ingredients = new List<RecipeIngredient>
                {
                    new() { Name = "ground beef", Quantity = 600, Unit = "g", SortOrder = 1 },
                    new() { Name = "hamburger buns", Quantity = 4, Unit = "pieces", SortOrder = 2 },
                    new() { Name = "Thai chili paste", Quantity = 2, Unit = "tbsp", SortOrder = 3 },
                    new() { Name = "fish sauce", Quantity = 1, Unit = "tbsp", SortOrder = 4 },
                    new() { Name = "lime juice", Quantity = 2, Unit = "tbsp", SortOrder = 5 },
                    new() { Name = "fresh cilantro", Quantity = 15, Unit = "g", SortOrder = 6 },
                    new() { Name = "fresh mint", Quantity = 10, Unit = "g", SortOrder = 7 },
                    new() { Name = "red onion", Quantity = 1, Unit = "piece", SortOrder = 8 },
                    new() { Name = "cucumber slices", Quantity = 100, Unit = "g", SortOrder = 9 },
                    new() { Name = "sriracha sauce", Quantity = 50, Unit = "g", SortOrder = 10 },
                    new() { Name = "salt and pepper", SortOrder = 11 }
                },
                Steps = new List<RecipeStep>
                {
                    new() { StepNumber = 1, Instruction = "Mix ground beef with Thai chili paste, fish sauce, lime juice, and chopped cilantro and mint." },
                    new() { StepNumber = 2, Instruction = "Form into 4 patties. Season with salt and pepper." },
                    new() { StepNumber = 3, Instruction = "Cook on a hot griddle for 4-5 minutes per side." },
                    new() { StepNumber = 4, Instruction = "Slice red onion thinly." },
                    new() { StepNumber = 5, Instruction = "Toast buns lightly." },
                    new() { StepNumber = 6, Instruction = "Assemble burgers with sriracha, burger patty, cucumber, and red onion. Garnish with herbs." }
                }
            },
            new Recipe
            {
                Title = "Red curry in coconut milk with chicken, pepper and sugar snap peas with rice",
                Servings = 3,
                PrepTimeMinutes = 15,
                CookTimeMinutes = 20,
                Tags = new List<string> { "curry", "Asian", "chicken" },
                Ingredients = new List<RecipeIngredient>
                {
                    new() { Name = "chicken breast", Quantity = 600, Unit = "g", SortOrder = 1 },
                    new() { Name = "white rice", Quantity = 300, Unit = "g", SortOrder = 2 },
                    new() { Name = "red curry paste", Quantity = 3, Unit = "tbsp", SortOrder = 3 },
                    new() { Name = "coconut milk", Quantity = 400, Unit = "ml", SortOrder = 4 },
                    new() { Name = "red bell pepper", Quantity = 2, Unit = "pieces", SortOrder = 5 },
                    new() { Name = "sugar snap peas", Quantity = 200, Unit = "g", SortOrder = 6 },
                    new() { Name = "bamboo shoots", Quantity = 100, Unit = "g", SortOrder = 7 },
                    new() { Name = "Thai basil", Quantity = 10, Unit = "g", SortOrder = 8 },
                    new() { Name = "fish sauce", Quantity = 1, Unit = "tbsp", SortOrder = 9 },
                    new() { Name = "vegetable oil", Quantity = 2, Unit = "tbsp", SortOrder = 10 },
                    new() { Name = "salt and pepper", SortOrder = 11 }
                },
                Steps = new List<RecipeStep>
                {
                    new() { StepNumber = 1, Instruction = "Cook rice according to package directions." },
                    new() { StepNumber = 2, Instruction = "Cube chicken breast. Slice peppers." },
                    new() { StepNumber = 3, Instruction = "Heat vegetable oil in a pot over medium heat." },
                    new() { StepNumber = 4, Instruction = "Add red curry paste and fry for 1-2 minutes until fragrant." },
                    new() { StepNumber = 5, Instruction = "Pour in coconut milk and bring to a simmer." },
                    new() { StepNumber = 6, Instruction = "Add chicken and cook for 10 minutes until cooked through." },
                    new() { StepNumber = 7, Instruction = "Add peppers, peas, and bamboo shoots. Simmer for 5 more minutes." },
                    new() { StepNumber = 8, Instruction = "Stir in fish sauce and fresh Thai basil. Season with salt and pepper." },
                    new() { StepNumber = 9, Instruction = "Serve over white rice." }
                }
            },
            new Recipe
            {
                Title = "Chili con carne",
                Servings = 6,
                PrepTimeMinutes = 15,
                CookTimeMinutes = 40,
                Tags = new List<string> { "Mexican", "stew", "comfort-food" },
                Ingredients = new List<RecipeIngredient>
                {
                    new() { Name = "ground beef", Quantity = 700, Unit = "g", SortOrder = 1 },
                    new() { Name = "canned kidney beans", Quantity = 400, Unit = "g", SortOrder = 2 },
                    new() { Name = "canned black beans", Quantity = 400, Unit = "g", SortOrder = 3 },
                    new() { Name = "canned diced tomatoes", Quantity = 400, Unit = "g", SortOrder = 4 },
                    new() { Name = "tomato paste", Quantity = 3, Unit = "tbsp", SortOrder = 5 },
                    new() { Name = "onion", Quantity = 2, Unit = "pieces", SortOrder = 6 },
                    new() { Name = "garlic cloves", Quantity = 3, Unit = "pieces", SortOrder = 7 },
                    new() { Name = "chili powder", Quantity = 3, Unit = "tbsp", SortOrder = 8 },
                    new() { Name = "cumin", Quantity = 2, Unit = "tsp", SortOrder = 9 },
                    new() { Name = "beef broth", Quantity = 300, Unit = "ml", SortOrder = 10 },
                    new() { Name = "olive oil", Quantity = 2, Unit = "tbsp", SortOrder = 11 },
                    new() { Name = "salt and pepper", SortOrder = 12 }
                },
                Steps = new List<RecipeStep>
                {
                    new() { StepNumber = 1, Instruction = "Sauté diced onion and garlic in olive oil until softened." },
                    new() { StepNumber = 2, Instruction = "Add ground beef and cook until browned, breaking it up as it cooks." },
                    new() { StepNumber = 3, Instruction = "Stir in chili powder and cumin. Cook for 1 minute." },
                    new() { StepNumber = 4, Instruction = "Add tomato paste and cook for 1 minute more." },
                    new() { StepNumber = 5, Instruction = "Stir in canned tomatoes, kidney beans, black beans, and beef broth." },
                    new() { StepNumber = 6, Instruction = "Bring to a boil, then reduce heat and simmer uncovered for 30-35 minutes." },
                    new() { StepNumber = 7, Instruction = "Season with salt and pepper to taste." },
                    new() { StepNumber = 8, Instruction = "Serve hot, optionally topped with sour cream, cheese, or cilantro." }
                }
            },
            new Recipe
            {
                Title = "Stuffed peppers with Thai minced meat and rice",
                Servings = 3,
                PrepTimeMinutes = 20,
                CookTimeMinutes = 30,
                Tags = new List<string> { "Thai", "oven", "vegetable" },
                Ingredients = new List<RecipeIngredient>
                {
                    new() { Name = "large bell peppers", Quantity = 4, Unit = "pieces", SortOrder = 1 },
                    new() { Name = "ground pork or beef", Quantity = 500, Unit = "g", SortOrder = 2 },
                    new() { Name = "white rice (cooked)", Quantity = 200, Unit = "g", SortOrder = 3 },
                    new() { Name = "Thai chili paste", Quantity = 2, Unit = "tbsp", SortOrder = 4 },
                    new() { Name = "fish sauce", Quantity = 1, Unit = "tbsp", SortOrder = 5 },
                    new() { Name = "lime juice", Quantity = 2, Unit = "tbsp", SortOrder = 6 },
                    new() { Name = "onion", Quantity = 1, Unit = "piece", SortOrder = 7 },
                    new() { Name = "garlic cloves", Quantity = 2, Unit = "pieces", SortOrder = 8 },
                    new() { Name = "fresh cilantro", Quantity = 10, Unit = "g", SortOrder = 9 },
                    new() { Name = "vegetable oil", Quantity = 2, Unit = "tbsp", SortOrder = 10 },
                    new() { Name = "salt and pepper", SortOrder = 11 }
                },
                Steps = new List<RecipeStep>
                {
                    new() { StepNumber = 1, Instruction = "Preheat oven to 180°C (350°F)." },
                    new() { StepNumber = 2, Instruction = "Cut tops off peppers and remove seeds and membranes." },
                    new() { StepNumber = 3, Instruction = "Heat vegetable oil and sauté diced onion and garlic." },
                    new() { StepNumber = 4, Instruction = "Add ground meat and cook until browned." },
                    new() { StepNumber = 5, Instruction = "Stir in Thai chili paste, fish sauce, lime juice, and cooked rice." },
                    new() { StepNumber = 6, Instruction = "Mix in fresh cilantro. Season with salt and pepper." },
                    new() { StepNumber = 7, Instruction = "Fill pepper cavities with meat and rice mixture." },
                    new() { StepNumber = 8, Instruction = "Place in a baking dish with a little water. Bake for 25-30 minutes until peppers are tender." }
                }
            },
            new Recipe
            {
                Title = "Belgian beef stew",
                Servings = 6,
                PrepTimeMinutes = 20,
                CookTimeMinutes = 120,
                Tags = new List<string> { "beef", "stew", "comfort-food", "Belgian" },
                Ingredients = new List<RecipeIngredient>
                {
                    new() { Name = "beef chuck or shoulder", Quantity = 1200, Unit = "g", SortOrder = 1 },
                    new() { Name = "onions", Quantity = 4, Unit = "pieces", SortOrder = 2 },
                    new() { Name = "garlic cloves", Quantity = 3, Unit = "pieces", SortOrder = 3 },
                    new() { Name = "dark beer", Quantity = 300, Unit = "ml", SortOrder = 4 },
                    new() { Name = "beef broth", Quantity = 400, Unit = "ml", SortOrder = 5 },
                    new() { Name = "tomato paste", Quantity = 2, Unit = "tbsp", SortOrder = 6 },
                    new() { Name = "bay leaves", Quantity = 2, Unit = "pieces", SortOrder = 7 },
                    new() { Name = "thyme", Quantity = 1, Unit = "tsp", SortOrder = 8 },
                    new() { Name = "olive oil", Quantity = 3, Unit = "tbsp", SortOrder = 9 },
                    new() { Name = "flour", Quantity = 2, Unit = "tbsp", SortOrder = 10 },
                    new() { Name = "salt and pepper", SortOrder = 11 }
                },
                Steps = new List<RecipeStep>
                {
                    new() { StepNumber = 1, Instruction = "Cube beef. Season with salt and pepper. Coat lightly with flour." },
                    new() { StepNumber = 2, Instruction = "Heat olive oil in a large pot over medium-high heat." },
                    new() { StepNumber = 3, Instruction = "Brown beef in batches. Remove and set aside." },
                    new() { StepNumber = 4, Instruction = "Sauté sliced onions and garlic in the same pot until softened." },
                    new() { StepNumber = 5, Instruction = "Pour in dark beer and scrape up browned bits from the bottom." },
                    new() { StepNumber = 6, Instruction = "Add beef broth, tomato paste, bay leaves, and thyme." },
                    new() { StepNumber = 7, Instruction = "Return beef to pot. Bring to a boil, then reduce heat to low." },
                    new() { StepNumber = 8, Instruction = "Cover and simmer for 1.5-2 hours until beef is very tender." },
                    new() { StepNumber = 9, Instruction = "Remove bay leaves. Season with salt and pepper. Serve hot." }
                }
            },
            new Recipe
            {
                Title = "Macaroni cheese with ham",
                Servings = 3,
                PrepTimeMinutes = 10,
                CookTimeMinutes = 20,
                Tags = new List<string> { "pasta", "comfort-food", "quick" },
                Ingredients = new List<RecipeIngredient>
                {
                    new() { Name = "elbow macaroni", Quantity = 400, Unit = "g", SortOrder = 1 },
                    new() { Name = "ham slices", Quantity = 200, Unit = "g", SortOrder = 2 },
                    new() { Name = "butter", Quantity = 3, Unit = "tbsp", SortOrder = 3 },
                    new() { Name = "all-purpose flour", Quantity = 3, Unit = "tbsp", SortOrder = 4 },
                    new() { Name = "whole milk", Quantity = 400, Unit = "ml", SortOrder = 5 },
                    new() { Name = "cheddar cheese", Quantity = 250, Unit = "g", SortOrder = 6 },
                    new() { Name = "salt and pepper", SortOrder = 7 },
                    new() { Name = "nutmeg", SortOrder = 8 }
                },
                Steps = new List<RecipeStep>
                {
                    new() { StepNumber = 1, Instruction = "Cook macaroni according to package directions. Drain and set aside." },
                    new() { StepNumber = 2, Instruction = "Chop ham into small pieces." },
                    new() { StepNumber = 3, Instruction = "Melt butter in a saucepan over medium heat." },
                    new() { StepNumber = 4, Instruction = "Whisk in flour and cook for 1 minute, stirring constantly." },
                    new() { StepNumber = 5, Instruction = "Gradually pour in milk while whisking to avoid lumps. Cook until thickened." },
                    new() { StepNumber = 6, Instruction = "Remove from heat and stir in grated cheddar cheese until melted. Season with salt, pepper, and nutmeg." },
                    new() { StepNumber = 7, Instruction = "Fold in cooked macaroni and ham." },
                    new() { StepNumber = 8, Instruction = "Serve hot." }
                }
            },
            new Recipe
            {
                Title = "Cowboy supper",
                Servings = 3,
                PrepTimeMinutes = 15,
                CookTimeMinutes = 20,
                Tags = new List<string> { "American", "quick", "hearty" },
                Ingredients = new List<RecipeIngredient>
                {
                    new() { Name = "ground beef", Quantity = 500, Unit = "g", SortOrder = 1 },
                    new() { Name = "potatoes", Quantity = 600, Unit = "g", SortOrder = 2 },
                    new() { Name = "canned baked beans", Quantity = 400, Unit = "g", SortOrder = 3 },
                    new() { Name = "bacon strips", Quantity = 150, Unit = "g", SortOrder = 4 },
                    new() { Name = "onion", Quantity = 1, Unit = "piece", SortOrder = 5 },
                    new() { Name = "garlic cloves", Quantity = 2, Unit = "pieces", SortOrder = 6 },
                    new() { Name = "cheddar cheese", Quantity = 100, Unit = "g", SortOrder = 7 },
                    new() { Name = "vegetable oil", Quantity = 2, Unit = "tbsp", SortOrder = 8 },
                    new() { Name = "salt and pepper", SortOrder = 9 }
                },
                Steps = new List<RecipeStep>
                {
                    new() { StepNumber = 1, Instruction = "Dice potatoes. Fry bacon until crispy. Remove and chop." },
                    new() { StepNumber = 2, Instruction = "In the bacon fat, cook diced potatoes until golden. Remove and set aside." },
                    new() { StepNumber = 3, Instruction = "Brown ground beef in the same pan with diced onion and garlic." },
                    new() { StepNumber = 4, Instruction = "Return potatoes and bacon to the pan." },
                    new() { StepNumber = 5, Instruction = "Stir in canned baked beans. Season with salt and pepper." },
                    new() { StepNumber = 6, Instruction = "Top with shredded cheddar cheese and serve hot." }
                }
            },
            new Recipe
            {
                Title = "Baked dish with mashed potato, spinach, minced meat and cheese sauce",
                Servings = 6,
                PrepTimeMinutes = 30,
                CookTimeMinutes = 40,
                Tags = new List<string> { "oven", "comfort-food", "vegetable" },
                Ingredients = new List<RecipeIngredient>
                {
                    new() { Name = "potatoes", Quantity = 1000, Unit = "g", SortOrder = 1 },
                    new() { Name = "fresh spinach", Quantity = 300, Unit = "g", SortOrder = 2 },
                    new() { Name = "ground beef", Quantity = 500, Unit = "g", SortOrder = 3 },
                    new() { Name = "butter", Quantity = 4, Unit = "tbsp", SortOrder = 4 },
                    new() { Name = "flour", Quantity = 4, Unit = "tbsp", SortOrder = 5 },
                    new() { Name = "milk", Quantity = 500, Unit = "ml", SortOrder = 6 },
                    new() { Name = "cheddar cheese", Quantity = 200, Unit = "g", SortOrder = 7 },
                    new() { Name = "onion", Quantity = 1, Unit = "piece", SortOrder = 8 },
                    new() { Name = "garlic clove", Quantity = 2, Unit = "pieces", SortOrder = 9 },
                    new() { Name = "olive oil", Quantity = 2, Unit = "tbsp", SortOrder = 10 },
                    new() { Name = "salt and pepper", SortOrder = 11 }
                },
                Steps = new List<RecipeStep>
                {
                    new() { StepNumber = 1, Instruction = "Preheat oven to 180°C (350°F)." },
                    new() { StepNumber = 2, Instruction = "Boil and mash potatoes with butter. Season with salt and pepper." },
                    new() { StepNumber = 3, Instruction = "Sauté spinach with diced onion and garlic in olive oil. Drain excess moisture." },
                    new() { StepNumber = 4, Instruction = "Brown ground beef in a separate pan. Season with salt and pepper." },
                    new() { StepNumber = 5, Instruction = "Make cheese sauce: melt 4 tbsp butter, whisk in flour, gradually add milk, stir in 150g grated cheese." },
                    new() { StepNumber = 6, Instruction = "Layer in a baking dish: ground beef, spinach, mashed potatoes, cheese sauce." },
                    new() { StepNumber = 7, Instruction = "Top with remaining grated cheese." },
                    new() { StepNumber = 8, Instruction = "Bake for 30-40 minutes until golden and bubbly." }
                }
            },
            new Recipe
            {
                Title = "Steak with pepper sauce",
                Servings = 3,
                PrepTimeMinutes = 10,
                CookTimeMinutes = 15,
                Tags = new List<string> { "beef", "quick", "elegant" },
                Ingredients = new List<RecipeIngredient>
                {
                    new() { Name = "beef steaks", Quantity = 4, Unit = "pieces", SortOrder = 1 },
                    new() { Name = "black peppercorns", Quantity = 2, Unit = "tbsp", SortOrder = 2 },
                    new() { Name = "butter", Quantity = 3, Unit = "tbsp", SortOrder = 3 },
                    new() { Name = "shallots", Quantity = 2, Unit = "pieces", SortOrder = 4 },
                    new() { Name = "dry white wine", Quantity = 100, Unit = "ml", SortOrder = 5 },
                    new() { Name = "beef broth", Quantity = 100, Unit = "ml", SortOrder = 6 },
                    new() { Name = "heavy cream", Quantity = 100, Unit = "ml", SortOrder = 7 },
                    new() { Name = "Dijon mustard", Quantity = 1, Unit = "tbsp", SortOrder = 8 },
                    new() { Name = "salt", SortOrder = 9 }
                },
                Steps = new List<RecipeStep>
                {
                    new() { StepNumber = 1, Instruction = "Crush peppercorns coarsely. Press into both sides of steaks." },
                    new() { StepNumber = 2, Instruction = "Melt 1 tbsp butter in a hot skillet. Cook steaks to desired doneness (rare to medium). Remove and rest." },
                    new() { StepNumber = 3, Instruction = "In the same pan, sauté minced shallots in remaining butter." },
                    new() { StepNumber = 4, Instruction = "Deglaze with white wine. Add beef broth and simmer for 2 minutes." },
                    new() { StepNumber = 5, Instruction = "Whisk in Dijon mustard and heavy cream. Simmer for 2-3 minutes." },
                    new() { StepNumber = 6, Instruction = "Season sauce with salt and pepper. Pour over steaks and serve." }
                }
            },
            new Recipe
            {
                Title = "Celery in tomato sauce with meatballs",
                Servings = 3,
                PrepTimeMinutes = 20,
                CookTimeMinutes = 30,
                Tags = new List<string> { "meatball", "Italian", "comfort-food" },
                Ingredients = new List<RecipeIngredient>
                {
                    new() { Name = "ground beef", Quantity = 500, Unit = "g", SortOrder = 1 },
                    new() { Name = "celery", Quantity = 3, Unit = "stalks", SortOrder = 2 },
                    new() { Name = "canned tomatoes", Quantity = 400, Unit = "g", SortOrder = 3 },
                    new() { Name = "tomato paste", Quantity = 2, Unit = "tbsp", SortOrder = 4 },
                    new() { Name = "breadcrumbs", Quantity = 100, Unit = "g", SortOrder = 5 },
                    new() { Name = "egg", Quantity = 1, Unit = "piece", SortOrder = 6 },
                    new() { Name = "parmesan cheese", Quantity = 50, Unit = "g", SortOrder = 7 },
                    new() { Name = "onion", Quantity = 1, Unit = "piece", SortOrder = 8 },
                    new() { Name = "garlic cloves", Quantity = 2, Unit = "pieces", SortOrder = 9 },
                    new() { Name = "olive oil", Quantity = 3, Unit = "tbsp", SortOrder = 10 },
                    new() { Name = "salt and pepper", SortOrder = 11 }
                },
                Steps = new List<RecipeStep>
                {
                    new() { StepNumber = 1, Instruction = "Mix ground beef with breadcrumbs, egg, grated parmesan, salt, and pepper." },
                    new() { StepNumber = 2, Instruction = "Form into 20-24 small meatballs." },
                    new() { StepNumber = 3, Instruction = "Brown meatballs in olive oil in a large pan. Remove and set aside." },
                    new() { StepNumber = 4, Instruction = "Dice celery, onion, and garlic. Sauté in the same pan until softened." },
                    new() { StepNumber = 5, Instruction = "Stir in tomato paste and cook for 1 minute." },
                    new() { StepNumber = 6, Instruction = "Add canned tomatoes. Return meatballs to pan." },
                    new() { StepNumber = 7, Instruction = "Simmer for 20 minutes until sauce thickens and meatballs are cooked through." },
                    new() { StepNumber = 8, Instruction = "Serve hot, optionally with pasta or crusty bread." }
                }
            },
            new Recipe
            {
                Title = "Pasta with celery, tomato, bacon, pesto and parmesan",
                Servings = 3,
                PrepTimeMinutes = 10,
                CookTimeMinutes = 20,
                Tags = new List<string> { "pasta", "Italian" },
                Ingredients = new List<RecipeIngredient>
                {
                    new() { Name = "pasta", Quantity = 400, Unit = "g", SortOrder = 1 },
                    new() { Name = "celery", Quantity = 2, Unit = "stalks", SortOrder = 2 },
                    new() { Name = "tomatoes", Quantity = 3, Unit = "pieces", SortOrder = 3 },
                    new() { Name = "bacon strips", Quantity = 150, Unit = "g", SortOrder = 4 },
                    new() { Name = "pesto", Quantity = 100, Unit = "g", SortOrder = 5 },
                    new() { Name = "parmesan cheese", Quantity = 100, Unit = "g", SortOrder = 6 },
                    new() { Name = "garlic cloves", Quantity = 2, Unit = "pieces", SortOrder = 7 },
                    new() { Name = "olive oil", Quantity = 2, Unit = "tbsp", SortOrder = 8 },
                    new() { Name = "salt and pepper", SortOrder = 9 },
                    new() { Name = "pine nuts", Quantity = 50, Unit = "g", SortOrder = 10 }
                },
                Steps = new List<RecipeStep>
                {
                    new() { StepNumber = 1, Instruction = "Cook pasta according to package directions. Drain and reserve 1 cup pasta water." },
                    new() { StepNumber = 2, Instruction = "Fry bacon until crispy. Remove and chop." },
                    new() { StepNumber = 3, Instruction = "Dice celery and tomatoes. Mince garlic." },
                    new() { StepNumber = 4, Instruction = "In the bacon fat, sauté celery and garlic for 2-3 minutes." },
                    new() { StepNumber = 5, Instruction = "Add diced tomatoes and cook for 3-4 minutes." },
                    new() { StepNumber = 6, Instruction = "Toss in cooked pasta, pesto, and bacon. Add pasta water as needed to loosen." },
                    new() { StepNumber = 7, Instruction = "Season with salt and pepper. Top with grated parmesan and toasted pine nuts." }
                }
            },
            new Recipe
            {
                Title = "Tagliatelle with fresh tomato sauce, diced pepper, cream and sambal",
                Servings = 3,
                PrepTimeMinutes = 15,
                CookTimeMinutes = 20,
                Tags = new List<string> { "pasta", "Italian", "oven" },
                Ingredients = new List<RecipeIngredient>
                {
                    new() { Name = "tagliatelle pasta", Quantity = 400, Unit = "g", SortOrder = 1 },
                    new() { Name = "fresh tomatoes", Quantity = 600, Unit = "g", SortOrder = 2 },
                    new() { Name = "red bell pepper", Quantity = 2, Unit = "pieces", SortOrder = 3 },
                    new() { Name = "heavy cream", Quantity = 200, Unit = "ml", SortOrder = 4 },
                    new() { Name = "sambal", Quantity = 1, Unit = "tsp", SortOrder = 5 },
                    new() { Name = "kalamata olives", Quantity = 100, Unit = "g", SortOrder = 6 },
                    new() { Name = "garlic cloves", Quantity = 2, Unit = "pieces", SortOrder = 7 },
                    new() { Name = "olive oil", Quantity = 3, Unit = "tbsp", SortOrder = 8 },
                    new() { Name = "basil leaves", Quantity = 10, Unit = "g", SortOrder = 9 },
                    new() { Name = "mozzarella cheese", Quantity = 100, Unit = "g", SortOrder = 10 },
                    new() { Name = "salt and pepper", SortOrder = 11 }
                },
                Steps = new List<RecipeStep>
                {
                    new() { StepNumber = 1, Instruction = "Preheat oven to 200°C (400°F)." },
                    new() { StepNumber = 2, Instruction = "Cook tagliatelle according to package directions. Drain." },
                    new() { StepNumber = 3, Instruction = "Blanch and peel fresh tomatoes. Chop coarsely. Dice bell peppers." },
                    new() { StepNumber = 4, Instruction = "Sauté minced garlic in olive oil. Add diced peppers and cook for 2 minutes." },
                    new() { StepNumber = 5, Instruction = "Add chopped tomatoes and simmer for 5 minutes." },
                    new() { StepNumber = 6, Instruction = "Stir in heavy cream and sambal. Add kalamata olives and torn basil." },
                    new() { StepNumber = 7, Instruction = "Toss pasta with sauce and transfer to a baking dish." },
                    new() { StepNumber = 8, Instruction = "Top with sliced mozzarella and bake for 10-12 minutes until cheese is melted." }
                }
            },
            new Recipe
            {
                Title = "Pork wok with red pepper, onion, eggs and sambal with rice",
                Servings = 3,
                PrepTimeMinutes = 15,
                CookTimeMinutes = 15,
                Tags = new List<string> { "wok", "Asian", "quick" },
                Ingredients = new List<RecipeIngredient>
                {
                    new() { Name = "pork shoulder", Quantity = 500, Unit = "g", SortOrder = 1 },
                    new() { Name = "white rice", Quantity = 300, Unit = "g", SortOrder = 2 },
                    new() { Name = "red bell pepper", Quantity = 2, Unit = "pieces", SortOrder = 3 },
                    new() { Name = "onion", Quantity = 2, Unit = "pieces", SortOrder = 4 },
                    new() { Name = "eggs", Quantity = 2, Unit = "pieces", SortOrder = 5 },
                    new() { Name = "sambal oelek", Quantity = 1, Unit = "tsp", SortOrder = 6 },
                    new() { Name = "soy sauce", Quantity = 2, Unit = "tbsp", SortOrder = 7 },
                    new() { Name = "garlic cloves", Quantity = 2, Unit = "pieces", SortOrder = 8 },
                    new() { Name = "vegetable oil", Quantity = 3, Unit = "tbsp", SortOrder = 9 },
                    new() { Name = "salt and pepper", SortOrder = 10 }
                },
                Steps = new List<RecipeStep>
                {
                    new() { StepNumber = 1, Instruction = "Cook rice according to package directions." },
                    new() { StepNumber = 2, Instruction = "Cube pork shoulder. Slice peppers and onion." },
                    new() { StepNumber = 3, Instruction = "Heat vegetable oil in a wok over high heat." },
                    new() { StepNumber = 4, Instruction = "Stir-fry pork cubes for 8-10 minutes until cooked. Remove and set aside." },
                    new() { StepNumber = 5, Instruction = "Stir-fry peppers, onion, and minced garlic for 3 minutes." },
                    new() { StepNumber = 6, Instruction = "Push vegetables to the side. Scramble eggs in the empty space." },
                    new() { StepNumber = 7, Instruction = "Return pork to wok. Add soy sauce and sambal. Mix well." },
                    new() { StepNumber = 8, Instruction = "Serve over white rice." }
                }
            },
            new Recipe
            {
                Title = "Quiche with cherry tomato, Italian ham and mozzarella",
                Servings = 6,
                PrepTimeMinutes = 20,
                CookTimeMinutes = 40,
                Tags = new List<string> { "quiche", "Italian", "oven" },
                Ingredients = new List<RecipeIngredient>
                {
                    new() { Name = "pie crust", Quantity = 1, Unit = "piece", SortOrder = 1 },
                    new() { Name = "eggs", Quantity = 4, Unit = "pieces", SortOrder = 2 },
                    new() { Name = "heavy cream", Quantity = 200, Unit = "ml", SortOrder = 3 },
                    new() { Name = "cherry tomatoes", Quantity = 250, Unit = "g", SortOrder = 4 },
                    new() { Name = "Italian ham", Quantity = 150, Unit = "g", SortOrder = 5 },
                    new() { Name = "mozzarella cheese", Quantity = 150, Unit = "g", SortOrder = 6 },
                    new() { Name = "basil leaves", Quantity = 10, Unit = "g", SortOrder = 7 },
                    new() { Name = "garlic clove", Quantity = 1, Unit = "piece", SortOrder = 8 },
                    new() { Name = "olive oil", Quantity = 2, Unit = "tbsp", SortOrder = 9 },
                    new() { Name = "salt and pepper", SortOrder = 10 }
                },
                Steps = new List<RecipeStep>
                {
                    new() { StepNumber = 1, Instruction = "Preheat oven to 200°C (400°F)." },
                    new() { StepNumber = 2, Instruction = "Place pie crust in a 9-inch tart pan." },
                    new() { StepNumber = 3, Instruction = "Halve cherry tomatoes. Chop Italian ham." },
                    new() { StepNumber = 4, Instruction = "Sauté tomatoes with minced garlic and basil in olive oil for 2 minutes. Cool slightly." },
                    new() { StepNumber = 5, Instruction = "Whisk eggs with heavy cream. Season with salt and pepper." },
                    new() { StepNumber = 6, Instruction = "Distribute ham, tomatoes, and torn mozzarella in the crust." },
                    new() { StepNumber = 7, Instruction = "Pour egg mixture over filling." },
                    new() { StepNumber = 8, Instruction = "Bake for 35-40 minutes until set and lightly golden." }
                }
            },
            new Recipe
            {
                Title = "Pasta with leek, ricotta, bacon and parmesan",
                Servings = 3,
                PrepTimeMinutes = 15,
                CookTimeMinutes = 20,
                Tags = new List<string> { "pasta", "vegetable" },
                Ingredients = new List<RecipeIngredient>
                {
                    new() { Name = "pasta", Quantity = 400, Unit = "g", SortOrder = 1 },
                    new() { Name = "leeks", Quantity = 3, Unit = "pieces", SortOrder = 2 },
                    new() { Name = "ricotta cheese", Quantity = 250, Unit = "g", SortOrder = 3 },
                    new() { Name = "bacon strips", Quantity = 150, Unit = "g", SortOrder = 4 },
                    new() { Name = "parmesan cheese", Quantity = 100, Unit = "g", SortOrder = 5 },
                    new() { Name = "garlic cloves", Quantity = 2, Unit = "pieces", SortOrder = 6 },
                    new() { Name = "butter", Quantity = 2, Unit = "tbsp", SortOrder = 7 },
                    new() { Name = "salt and pepper", SortOrder = 8 },
                    new() { Name = "nutmeg", SortOrder = 9 }
                },
                Steps = new List<RecipeStep>
                {
                    new() { StepNumber = 1, Instruction = "Cook pasta according to package directions. Drain." },
                    new() { StepNumber = 2, Instruction = "Slice leeks into rings (white and light green parts). Rinse to remove dirt." },
                    new() { StepNumber = 3, Instruction = "Fry bacon until crispy. Remove and chop." },
                    new() { StepNumber = 4, Instruction = "In the bacon fat, sauté leeks and minced garlic for 4-5 minutes until soft." },
                    new() { StepNumber = 5, Instruction = "Mix ricotta with grated parmesan. Season with salt, pepper, and a pinch of nutmeg." },
                    new() { StepNumber = 6, Instruction = "Toss pasta with leek mixture and ricotta mixture. Add chopped bacon." },
                    new() { StepNumber = 7, Instruction = "Drizzle with butter and serve hot." }
                }
            },
            new Recipe
            {
                Title = "Taco quinoa with black beans and cheddar",
                Servings = 3,
                PrepTimeMinutes = 15,
                CookTimeMinutes = 20,
                Tags = new List<string> { "Mexican", "vegetarian", "quick" },
                Ingredients = new List<RecipeIngredient>
                {
                    new() { Name = "quinoa", Quantity = 200, Unit = "g", SortOrder = 1 },
                    new() { Name = "canned black beans", Quantity = 400, Unit = "g", SortOrder = 2 },
                    new() { Name = "cheddar cheese", Quantity = 150, Unit = "g", SortOrder = 3 },
                    new() { Name = "corn kernels", Quantity = 200, Unit = "g", SortOrder = 4 },
                    new() { Name = "tomato", Quantity = 2, Unit = "pieces", SortOrder = 5 },
                    new() { Name = "red onion", Quantity = 1, Unit = "piece", SortOrder = 6 },
                    new() { Name = "cilantro", Quantity = 15, Unit = "g", SortOrder = 7 },
                    new() { Name = "lime juice", Quantity = 2, Unit = "tbsp", SortOrder = 8 },
                    new() { Name = "olive oil", Quantity = 2, Unit = "tbsp", SortOrder = 9 },
                    new() { Name = "cumin", Quantity = 1, Unit = "tsp", SortOrder = 10 },
                    new() { Name = "chili powder", Quantity = 1, Unit = "tsp", SortOrder = 11 },
                    new() { Name = "salt and pepper", SortOrder = 12 }
                },
                Steps = new List<RecipeStep>
                {
                    new() { StepNumber = 1, Instruction = "Cook quinoa according to package directions." },
                    new() { StepNumber = 2, Instruction = "Warm black beans in a pot with cumin and chili powder." },
                    new() { StepNumber = 3, Instruction = "Dice tomato and red onion. Chop fresh cilantro." },
                    new() { StepNumber = 4, Instruction = "Mix cooked quinoa with beans, corn, tomato, and onion." },
                    new() { StepNumber = 5, Instruction = "Dress with olive oil and lime juice. Season with salt and pepper." },
                    new() { StepNumber = 6, Instruction = "Top with shredded cheddar cheese and fresh cilantro." },
                    new() { StepNumber = 7, Instruction = "Serve at room temperature or slightly warmed." }
                }
            },
            new Recipe
            {
                Title = "Quinoa with ricotta, cooked chicken, lemon, green beans and parmesan",
                Servings = 3,
                PrepTimeMinutes = 15,
                CookTimeMinutes = 20,
                Tags = new List<string> { "quinoa", "chicken", "healthy" },
                Ingredients = new List<RecipeIngredient>
                {
                    new() { Name = "quinoa", Quantity = 200, Unit = "g", SortOrder = 1 },
                    new() { Name = "cooked chicken breast", Quantity = 300, Unit = "g", SortOrder = 2 },
                    new() { Name = "ricotta cheese", Quantity = 200, Unit = "g", SortOrder = 3 },
                    new() { Name = "green beans", Quantity = 200, Unit = "g", SortOrder = 4 },
                    new() { Name = "parmesan cheese", Quantity = 100, Unit = "g", SortOrder = 5 },
                    new() { Name = "lemon juice", Quantity = 3, Unit = "tbsp", SortOrder = 6 },
                    new() { Name = "lemon zest", Quantity = 1, Unit = "tsp", SortOrder = 7 },
                    new() { Name = "olive oil", Quantity = 3, Unit = "tbsp", SortOrder = 8 },
                    new() { Name = "garlic clove", Quantity = 1, Unit = "piece", SortOrder = 9 },
                    new() { Name = "fresh parsley", Quantity = 10, Unit = "g", SortOrder = 10 },
                    new() { Name = "salt and pepper", SortOrder = 11 }
                },
                Steps = new List<RecipeStep>
                {
                    new() { StepNumber = 1, Instruction = "Cook quinoa according to package directions." },
                    new() { StepNumber = 2, Instruction = "Trim and blanch green beans for 3-4 minutes. Drain." },
                    new() { StepNumber = 3, Instruction = "Shred or cube cooked chicken breast." },
                    new() { StepNumber = 4, Instruction = "Mix ricotta with lemon juice, lemon zest, minced garlic, and grated parmesan." },
                    new() { StepNumber = 5, Instruction = "Toss cooked quinoa with green beans, chicken, and ricotta mixture." },
                    new() { StepNumber = 6, Instruction = "Drizzle with olive oil. Season with salt and pepper." },
                    new() { StepNumber = 7, Instruction = "Garnish with fresh parsley and serve at room temperature." }
                }
            },
            new Recipe
            {
                Title = "Pita bread with falafel and tabbouleh",
                Servings = 3,
                PrepTimeMinutes = 20,
                CookTimeMinutes = 20,
                Tags = new List<string> { "Middle Eastern", "vegetarian" },
                Ingredients = new List<RecipeIngredient>
                {
                    new() { Name = "chickpeas (canned)", Quantity = 400, Unit = "g", SortOrder = 1 },
                    new() { Name = "pita bread", Quantity = 4, Unit = "pieces", SortOrder = 2 },
                    new() { Name = "bulgur wheat", Quantity = 150, Unit = "g", SortOrder = 3 },
                    new() { Name = "parsley", Quantity = 50, Unit = "g", SortOrder = 4 },
                    new() { Name = "mint", Quantity = 20, Unit = "g", SortOrder = 5 },
                    new() { Name = "tomato", Quantity = 2, Unit = "pieces", SortOrder = 6 },
                    new() { Name = "red onion", Quantity = 1, Unit = "piece", SortOrder = 7 },
                    new() { Name = "cucumber", Quantity = 1, Unit = "piece", SortOrder = 8 },
                    new() { Name = "garlic cloves", Quantity = 2, Unit = "pieces", SortOrder = 9 },
                    new() { Name = "cumin", Quantity = 1, Unit = "tsp", SortOrder = 10 },
                    new() { Name = "coriander", Quantity = 1, Unit = "tsp", SortOrder = 11 },
                    new() { Name = "flour", Quantity = 3, Unit = "tbsp", SortOrder = 12 },
                    new() { Name = "olive oil", Quantity = 3, Unit = "tbsp", SortOrder = 13 },
                    new() { Name = "lemon juice", Quantity = 3, Unit = "tbsp", SortOrder = 14 },
                    new() { Name = "salt and pepper", SortOrder = 15 }
                },
                Steps = new List<RecipeStep>
                {
                    new() { StepNumber = 1, Instruction = "Soak bulgur in hot water for 15 minutes. Drain well." },
                    new() { StepNumber = 2, Instruction = "Chop parsley, mint, tomato, and red onion finely. Mix with bulgur, 2 tbsp olive oil, and 2 tbsp lemon juice. Season with salt and pepper." },
                    new() { StepNumber = 3, Instruction = "Drain chickpeas. Blend with garlic, cumin, coriander, flour, salt, and pepper until slightly chunky." },
                    new() { StepNumber = 4, Instruction = "Form into 12-16 falafel balls. Fry in olive oil until golden on all sides." },
                    new() { StepNumber = 5, Instruction = "Dice cucumber." },
                    new() { StepNumber = 6, Instruction = "Warm pita bread. Fill with falafel, taboulé, and diced cucumber." },
                    new() { StepNumber = 7, Instruction = "Serve with tahini or yogurt sauce if desired." }
                }
            },
            new Recipe
            {
                Title = "Tortilla with goat cheese, bacon and apple",
                Servings = 3,
                PrepTimeMinutes = 10,
                CookTimeMinutes = 10,
                Tags = new List<string> { "quick", "vegetarian-option" },
                Ingredients = new List<RecipeIngredient>
                {
                    new() { Name = "flour tortillas", Quantity = 4, Unit = "pieces", SortOrder = 1 },
                    new() { Name = "goat cheese", Quantity = 150, Unit = "g", SortOrder = 2 },
                    new() { Name = "bacon strips", Quantity = 150, Unit = "g", SortOrder = 3 },
                    new() { Name = "apples", Quantity = 2, Unit = "pieces", SortOrder = 4 },
                    new() { Name = "fresh spinach", Quantity = 100, Unit = "g", SortOrder = 5 },
                    new() { Name = "honey", Quantity = 2, Unit = "tbsp", SortOrder = 6 },
                    new() { Name = "Dijon mustard", Quantity = 1, Unit = "tbsp", SortOrder = 7 },
                    new() { Name = "salt and pepper", SortOrder = 8 }
                },
                Steps = new List<RecipeStep>
                {
                    new() { StepNumber = 1, Instruction = "Fry bacon until crispy. Chop and set aside." },
                    new() { StepNumber = 2, Instruction = "Core and slice apples thinly." },
                    new() { StepNumber = 3, Instruction = "Spread Dijon mustard on each tortilla." },
                    new() { StepNumber = 4, Instruction = "Layer spinach, apple slices, crumbled goat cheese, and bacon on each tortilla." },
                    new() { StepNumber = 5, Instruction = "Drizzle with honey." },
                    new() { StepNumber = 6, Instruction = "Warm in a skillet for 2-3 minutes until cheese softens, or wrap in foil and warm in the oven at 160°C for 5 minutes." },
                    new() { StepNumber = 7, Instruction = "Season with salt and pepper. Fold or roll and serve." }
                }
            }
        };
    }
}
