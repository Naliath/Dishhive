import { RecipeSummary, Recipe, ImportedRecipe } from '../models/recipe.model';
import { FamilyMember, FamilyMemberSummary } from '../models/family-member.model';
import { WeekPlan, PlannedMeal } from '../models/week-plan.model';

// -------------------------------------------------------------------------
// Recipe builders
// -------------------------------------------------------------------------

export class RecipeBuilder {
  private recipe: Partial<RecipeSummary> = {
    id: 'recipe-1',
    title: 'Test Recipe',
    servings: 4,
    tags: [],
    createdAt: new Date().toISOString(),
  };

  withId(id: string): this { this.recipe.id = id; return this; }
  withTitle(title: string): this { this.recipe.title = title; return this; }
  withServings(servings: number): this { this.recipe.servings = servings; return this; }
  withPictureUrl(url: string): this { this.recipe.pictureUrl = url; return this; }
  withTags(...tags: string[]): this { this.recipe.tags = tags; return this; }

  build(): RecipeSummary {
    return this.recipe as RecipeSummary;
  }

  static create(): RecipeBuilder { return new RecipeBuilder(); }

  static createMany(count: number): RecipeSummary[] {
    return Array.from({ length: count }, (_, i) =>
      new RecipeBuilder().withId(`recipe-${i + 1}`).withTitle(`Recipe ${i + 1}`).build()
    );
  }
}

// -------------------------------------------------------------------------
// FamilyMember builders
// -------------------------------------------------------------------------

export class FamilyMemberBuilder {
  private member: Partial<FamilyMemberSummary> = {
    id: 'member-1',
    name: 'Test Member',
    isGuest: false,
  };

  withId(id: string): this { this.member.id = id; return this; }
  withName(name: string): this { this.member.name = name; return this; }
  asGuest(): this { this.member.isGuest = true; return this; }

  build(): FamilyMemberSummary {
    return this.member as FamilyMemberSummary;
  }

  static create(): FamilyMemberBuilder { return new FamilyMemberBuilder(); }
}
