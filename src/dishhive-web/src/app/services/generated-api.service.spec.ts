import { HttpErrorResponse, provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { firstValueFrom } from 'rxjs';
import { afterEach, beforeEach, describe, expect, it } from 'vitest';
import { RecipesService } from './recipes.service';

describe('Generated recipe API boundary', () => {
  let service: RecipesService;
  let http: HttpTestingController;
  beforeEach(() => {
    TestBed.configureTestingModule({ providers: [provideHttpClient(), provideHttpClientTesting()] });
    service = TestBed.inject(RecipesService);
    http = TestBed.inject(HttpTestingController);
  });
  afterEach(() => http.verify());

  it('keeps conflict status and problem details so the editor can retain the draft', async () => {
    const result = firstValueFrom(service.deleteRecipe('recipe')).catch(error => error as HttpErrorResponse);
    const problem = { status: 409, code: 'concurrent_edit', title: 'Changed', detail: 'Reload and review', traceId: 'trace' };
    http.expectOne('/api/recipes/recipe').flush(new Blob([JSON.stringify(problem)]), { status: 409, statusText: 'Conflict' });
    const error = await result;
    expect(error).toBeInstanceOf(HttpErrorResponse);
    expect((error as HttpErrorResponse).status).toBe(409);
    expect((error as HttpErrorResponse).error).toEqual(problem);
  });

  it('reads pagination from the generated response contract', async () => {
    const result = firstValueFrom(service.getRecipesPage(' soup ', undefined, [], undefined, 2, 12));
    const request = http.expectOne(request => request.url.split('?')[0] === '/api/recipes/page');
    const query = new URL(request.request.url, 'http://localhost').searchParams;
    expect(query.get('search')).toBe('soup');
    expect(query.get('page')).toBe('2');
    expect(query.has('category')).toBe(false);
    const page = { items: [], totalCount: 25, page: 2, pageSize: 12 };
    request.flush(new Blob([JSON.stringify(page)]));
    expect(await result).toEqual(page);
  });

  it('uploads a file using generated multipart handling', async () => {
    const result = firstValueFrom(service.setRecipeImage('recipe', new File(['photo'], 'photo.png', { type: 'image/png' })));
    const request = http.expectOne('/api/recipes/recipe/image');
    expect(request.request.method).toBe('PUT');
    expect(request.request.body).toBeInstanceOf(FormData);
    expect((request.request.body as FormData).get('file')).toBeInstanceOf(File);
    expect(request.request.headers.has('Content-Type')).toBe(false);
    request.flush(null, { status: 204, statusText: 'No Content' });
    await result;
  });
});
