import { ChangeDetectionStrategy, Component, OnInit } from '@angular/core';
import { MatButtonModule } from '@angular/material/button';
import { MatCardModule } from '@angular/material/card';
import { MatIconModule } from '@angular/material/icon';
import { MatRadioModule } from '@angular/material/radio';
import { MatSnackBar, MatSnackBarModule } from '@angular/material/snack-bar';
import { SettingsService } from '../../services/settings.service';
import { PwaService } from '../../services/pwa.service';
import { IntegrationsStatusComponent } from '../../components/integrations-status/integrations-status';
import { MeasurementSystem } from '../../models/user-setting.model';
import { environment } from '../../../environments/environment';

@Component({
  selector: 'app-settings-page',
  standalone: true,
  imports: [MatButtonModule, MatCardModule, MatIconModule, MatRadioModule, MatSnackBarModule, IntegrationsStatusComponent],
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './settings.page.html',
  styleUrl: './settings.page.scss'
})
export class SettingsPage implements OnInit {
  readonly version = environment.version;

  constructor(
    public settingsService: SettingsService,
    public pwaService: PwaService,
    private snackBar: MatSnackBar
  ) {}

  ngOnInit(): void {
    this.settingsService.loadMeasurementSystem().subscribe();
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
