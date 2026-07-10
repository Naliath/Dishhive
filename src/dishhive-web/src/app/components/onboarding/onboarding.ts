import { ChangeDetectionStrategy, Component, OnInit, output, signal } from '@angular/core';
import { FormControl, FormGroup, FormGroupDirective, ReactiveFormsModule, Validators } from '@angular/forms';
import { MatButtonModule } from '@angular/material/button';
import { MatCardModule } from '@angular/material/card';
import { ErrorStateMatcher } from '@angular/material/core';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatIconModule } from '@angular/material/icon';
import { MatInputModule } from '@angular/material/input';
import { MatListModule } from '@angular/material/list';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';
import { MatRadioModule } from '@angular/material/radio';
import { MatSlideToggleModule } from '@angular/material/slide-toggle';
import { MatSnackBar, MatSnackBarModule } from '@angular/material/snack-bar';
import { MatStepper, MatStepperModule } from '@angular/material/stepper';
import { forkJoin } from 'rxjs';
import { FamilyMember } from '../../models/family-member.model';
import { FirstDayOfWeek, MeasurementSystem } from '../../models/user-setting.model';
import { FamilyMembersService } from '../../services/family-members.service';
import { OnboardingService } from '../../services/onboarding.service';
import { SettingsService } from '../../services/settings.service';

@Component({
  selector: 'app-onboarding',
  standalone: true,
  imports: [
    ReactiveFormsModule,
    MatButtonModule,
    MatCardModule,
    MatFormFieldModule,
    MatIconModule,
    MatInputModule,
    MatListModule,
    MatProgressSpinnerModule,
    MatRadioModule,
    MatSlideToggleModule,
    MatSnackBarModule,
    MatStepperModule
  ],
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './onboarding.html',
  styleUrl: './onboarding.scss'
})
export class OnboardingComponent implements OnInit {
  readonly finished = output<void>();

  readonly preferencesForm = new FormGroup({
    firstDayOfWeek: new FormControl<FirstDayOfWeek>('monday', { nonNullable: true }),
    measurementSystem: new FormControl<MeasurementSystem>('metric', { nonNullable: true })
  });
  readonly memberForm = new FormGroup({
    name: new FormControl('', {
      nonNullable: true,
      validators: [Validators.required, Validators.maxLength(100)]
    }),
    isGuest: new FormControl(false, { nonNullable: true })
  });

  readonly members = signal<FamilyMember[]>([]);
  readonly memberSubmissionAttempted = signal(false);
  readonly savingPreferences = signal(false);
  readonly addingMember = signal(false);
  readonly finishing = signal(false);
  readonly errorMessage = signal<string | null>(null);
  readonly memberErrorStateMatcher: ErrorStateMatcher = {
    isErrorState: control => !!control?.invalid && this.memberSubmissionAttempted()
  };

  constructor(
    private readonly settingsService: SettingsService,
    private readonly familyMembersService: FamilyMembersService,
    private readonly onboardingService: OnboardingService,
    private readonly snackBar: MatSnackBar
  ) {}

  ngOnInit(): void {
    forkJoin([
      this.settingsService.loadMeasurementSystem(),
      this.settingsService.loadFirstDayOfWeek()
    ]).subscribe(([measurementSystem, firstDayOfWeek]) => {
      this.preferencesForm.setValue({ measurementSystem, firstDayOfWeek });
    });

    this.familyMembersService.getMembers().subscribe({
      next: members => this.members.set(members),
      error: () => this.errorMessage.set('Saved household members could not be loaded.')
    });
  }

  savePreferences(stepper: MatStepper): void {
    if (this.preferencesForm.invalid || this.savingPreferences()) {
      return;
    }

    this.errorMessage.set(null);
    this.savingPreferences.set(true);
    const { measurementSystem, firstDayOfWeek } = this.preferencesForm.getRawValue();

    forkJoin([
      this.settingsService.setMeasurementSystem(measurementSystem),
      this.settingsService.setFirstDayOfWeek(firstDayOfWeek)
    ]).subscribe({
      next: () => {
        this.savingPreferences.set(false);
        stepper.next();
      },
      error: () => {
        this.savingPreferences.set(false);
        this.errorMessage.set('Your preferences could not be saved. Please try again.');
      }
    });
  }

  addMember(formDirective: FormGroupDirective): void {
    if (this.addingMember()) {
      return;
    }

    this.memberSubmissionAttempted.set(true);
    const { name, isGuest } = this.memberForm.getRawValue();
    const trimmedName = name.trim();
    if (this.memberForm.invalid || !trimmedName) {
      if (!trimmedName) {
        this.memberForm.controls.name.setErrors({ required: true });
      }
      return;
    }

    this.errorMessage.set(null);
    this.addingMember.set(true);

    this.familyMembersService.createMember({
      name: trimmedName,
      isGuest,
      allergyTags: [],
      dietTags: []
    }).subscribe({
      next: member => {
        this.members.update(members => [...members, member]);
        this.memberSubmissionAttempted.set(false);
        formDirective.resetForm({ name: '', isGuest: false });
        this.addingMember.set(false);
        this.snackBar.open(`${member.name} added`, 'Dismiss', { duration: 2500 });
      },
      error: () => {
        this.addingMember.set(false);
        this.errorMessage.set('That household member could not be added. Please try again.');
      }
    });
  }

  skip(): void {
    this.endOnboarding(true);
  }

  finish(): void {
    this.endOnboarding(false);
  }

  private endOnboarding(skipped: boolean): void {
    if (this.finishing()) {
      return;
    }

    this.errorMessage.set(null);
    this.finishing.set(true);
    this.onboardingService.complete(skipped).subscribe({
      next: () => this.finished.emit(),
      error: () => {
        this.finishing.set(false);
        this.errorMessage.set('Setup could not be closed. Please try again.');
      }
    });
  }
}
