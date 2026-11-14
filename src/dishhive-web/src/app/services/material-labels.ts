import { effect, inject } from '@angular/core';
import { MatPaginatorIntl } from '@angular/material/paginator';
import { LanguageService } from './language.service';

export function localizedPaginator(): MatPaginatorIntl {
  const language = inject(LanguageService);
  const labels = new MatPaginatorIntl();
  labels.getRangeLabel = (page, size, length) => {
    const total = Math.max(0, length);
    if (!size || !total) return language.t('paginator.emptyRange', { total });
    const start = page * size;
    return language.t('paginator.range', { start: start + 1, end: Math.min(start + size, total), total });
  };
  effect(() => {
    labels.itemsPerPageLabel = language.t('paginator.itemsPerPage');
    labels.nextPageLabel = language.t('paginator.nextPage');
    labels.previousPageLabel = language.t('paginator.previousPage');
    labels.firstPageLabel = language.t('paginator.firstPage');
    labels.lastPageLabel = language.t('paginator.lastPage');
    labels.changes.next();
  });
  return labels;
}
