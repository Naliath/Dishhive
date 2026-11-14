import { signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { of, Subject } from 'rxjs';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import { CookbooksService } from '../../services/cookbooks.service';
import { FamilyMembersService } from '../../services/family-members.service';
import { LanguageService } from '../../services/language.service';
import { RecipesService } from '../../services/recipes.service';
import { StatisticsService } from '../../services/statistics.service';
import { RecipeListItem } from '../../models/recipe.model';
import { RecipesPage } from './recipes.page';

describe('RecipesPage latest reads', () => {
  const requests: Subject<{ items: RecipeListItem[]; totalCount: number }>[] = [];

  beforeEach(async () => {
    requests.length = 0;
    await TestBed.configureTestingModule({
      imports: [RecipesPage],
      providers: [
        provideRouter([]),
        { provide: RecipesService, useValue: {
          getRecipesPage: vi.fn(() => {
            const request = new Subject<{ items: RecipeListItem[]; totalCount: number }>();
            requests.push(request);
            return request;
          }),
          getCategories: () => of([]),
          getRecipeTags: () => of([])
        } },
        { provide: CookbooksService, useValue: { getCookbooks: () => of([]) } },
        { provide: FamilyMembersService, useValue: { getMembers: () => of([]) } },
        { provide: StatisticsService, useValue: { getDishStatistics: () => of({ dishes: [] }) } },
        { provide: LanguageService, useValue: { language: signal('en'), t: (key: string) => key } }
      ]
    }).compileComponents();
  });

  it('does not let an older filter response replace newer results', () => {
    const fixture = TestBed.createComponent(RecipesPage);
    fixture.detectChanges();
    fixture.componentInstance.loadRecipes();

    const newer = recipe('newer');
    requests[1].next({ items: [newer], totalCount: 1 });
    requests[0].next({ items: [recipe('older')], totalCount: 1 });

    expect(fixture.componentInstance.recipes()).toEqual([newer]);
  });

  function recipe(id: string): RecipeListItem {
    return { id, title: id, servings: 4, hasLocalImage: false, tags: [], cookbookIds: [] };
  }
});
