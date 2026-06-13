import { ChangeDetectionStrategy, Component, OnInit, signal } from '@angular/core';
import { MatButtonModule } from '@angular/material/button';
import { MatCardModule } from '@angular/material/card';
import { MatIconModule } from '@angular/material/icon';
import { MatRadioModule } from '@angular/material/radio';
import { MatSlideToggleModule } from '@angular/material/slide-toggle';
import { MatSnackBar, MatSnackBarModule } from '@angular/material/snack-bar';
import { SettingsService } from '../../services/settings.service';
import { PwaService } from '../../services/pwa.service';
import { RecipesService } from '../../services/recipes.service';
import { CookbooksService } from '../../services/cookbooks.service';
import { IntegrationsStatusComponent } from '../../components/integrations-status/integrations-status';
import { MeasurementSystem } from '../../models/user-setting.model';
import { AutoCollectionInfo } from '../../models/recipe.model';
import { environment } from '../../../environments/environment';

@Component({
  selector: 'app-settings-page',
  standalone: true,
  imports: [
    MatButtonModule, MatCardModule, MatIconModule, MatRadioModule,
    MatSlideToggleModule, MatSnackBarModule, IntegrationsStatusComponent
  ],
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './settings.page.html',
  styleUrl: './settings.page.scss'
})
export class SettingsPage implements OnInit {
  readonly version = environment.version;

  readonly importing = signal(false);
  readonly importSkipped = signal<{ title: string; reason: string }[]>([]);

  readonly autoCollections = signal<AutoCollectionInfo[]>([]);

  constructor(
    public settingsService: SettingsService,
    public pwaService: PwaService,
    public recipesService: RecipesService,
    private cookbooksService: CookbooksService,
    private snackBar: MatSnackBar
  ) {}

  ngOnInit(): void {
    this.settingsService.loadMeasurementSystem().subscribe();
    this.loadAutoCollections();
  }

  private loadAutoCollections(): void {
    this.cookbooksService.getAutoCollections().subscribe({
      next: collections => this.autoCollections.set(collections),
      error: () => { /* the section just stays empty */ }
    });
  }

  toggleAutoCollection(collection: AutoCollectionInfo, enabled: boolean): void {
    // optimistic flip; revert on error
    this.autoCollections.update(list =>
      list.map(c => c.id === collection.id ? { ...c, enabled } : c));
    this.cookbooksService.setAutoCollectionEnabled(collection.id, enabled).subscribe({
      error: () => {
        this.autoCollections.update(list =>
          list.map(c => c.id === collection.id ? { ...c, enabled: !enabled } : c));
        this.snackBar.open('Could not update the collection', 'Dismiss', { duration: 4000 });
      }
    });
  }

  importRecipes(files: FileList | null): void {
    const file = files?.item(0);
    if (!file) {
      return;
    }

    this.importing.set(true);
    this.importSkipped.set([]);
    this.recipesService.importRecipesFile(file).subscribe({
      next: result => {
        this.importing.set(false);
        this.importSkipped.set(result.skippedRecipes);
        const parts = [
          result.created > 0 ? `${result.created} added` : null,
          result.updated > 0 ? `${result.updated} updated` : null,
          result.skipped > 0 ? `${result.skipped} skipped` : null
        ].filter(p => p !== null);
        this.snackBar.open(`Import finished: ${parts.join(', ') || 'nothing to do'}`, 'Dismiss', { duration: 6000 });
      },
      error: err => {
        this.importing.set(false);
        const detail = err?.error?.detail ?? 'Is it a recipe JSON file?';
        this.snackBar.open(`Could not import the file. ${detail}`, 'Dismiss', { duration: 6000 });
      }
    });
  }

  installApp(): void {
    this.pwaService.promptInstall().then(accepted => {
      if (accepted) {
        this.snackBar.open('Dishhive installed', 'Dismiss', { duration: 3000 });
      }
    });
  }

  setMeasurementSystem(system: MeasurementSystem): void {
    this.settingsService.setMeasurementSystem(system).subscribe({
      next: () => this.snackBar.open(`Measurement system set to ${system}`, 'Dismiss', { duration: 3000 }),
      error: () => this.snackBar.open('Could not save the setting', 'Dismiss', { duration: 4000 })
    });
  }
}
