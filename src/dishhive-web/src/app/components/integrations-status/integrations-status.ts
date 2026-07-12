import { ChangeDetectionStrategy, Component, OnInit, computed, signal } from '@angular/core';
import { RouterLink } from '@angular/router';
import { DatePipe, DecimalPipe } from '@angular/common';
import { MatButtonModule } from '@angular/material/button';
import { MatCardModule } from '@angular/material/card';
import { MatIconModule } from '@angular/material/icon';
import { MatTooltipModule } from '@angular/material/tooltip';
import { TranslatePipe } from '../../services/language.service';
import { CookingLoaderComponent } from '../cooking-loader/cooking-loader';
import { IntegrationsService } from '../../services/integrations.service';
import {
  AiModelTestCheck,
  AiModelTestStatus,
  AiModelTestVerdict,
  IntegrationStatusResponse,
  ScraperVersionCheck
} from '../../models/integration-status.model';

@Component({
  selector: 'app-integrations-status',
  standalone: true,
  imports: [CookingLoaderComponent, DatePipe, DecimalPipe, MatButtonModule, MatCardModule, MatIconModule,
    MatTooltipModule, RouterLink, TranslatePipe],
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './integrations-status.html',
  styleUrl: './integrations-status.scss'
})
export class IntegrationsStatusComponent implements OnInit {
  private static readonly updatePollIntervalMs = 2000;
  private static readonly updatePollAttempts = 15;
  // The model test makes up to two full completions (each bounded by Ai__TimeoutSeconds,
  // default 60s) plus the /models probe — poll for up to 5 minutes before giving up
  private static readonly aiTestPollAttempts = 150;

  readonly integrations = signal<IntegrationStatusResponse | null>(null);
  readonly versionCheck = signal<ScraperVersionCheck | null>(null);
  readonly checkingVersion = signal(false);
  readonly updating = signal(false);
  readonly scraperMessage = signal<string | null>(null);
  readonly aiTest = signal<AiModelTestStatus | null>(null);
  readonly aiTestRunning = computed(() => this.aiTest()?.state === 'running');
  /** True from the button click until the start request answers — covers the window
   *  before the server reports "running", so the button can't be double-clicked */
  readonly aiTestStarting = signal(false);
  readonly aiTestBusy = computed(() => this.aiTestRunning() || this.aiTestStarting());

  constructor(private integrationsService: IntegrationsService) {}

  ngOnInit(): void {
    this.integrationsService.getStatus().subscribe(status => {
      this.integrations.set(status);
      if (status?.ai.configured) {
        this.integrationsService.getAiModelTest().subscribe(test => {
          this.aiTest.set(test);
          // Pick up a test still running from startup
          if (test?.state === 'running') {
            this.pollAiTest(IntegrationsStatusComponent.aiTestPollAttempts);
          }
        });
      }
    });
  }

  /** Kicks off a fresh model capability test (after the user tweaked model settings).
   *  No-op while one is already running — the server joins concurrent starts anyway,
   *  but the button should not even pretend to start another. */
  runAiTest(): void {
    if (this.aiTestBusy()) {
      return;
    }
    this.aiTestStarting.set(true);
    this.integrationsService.runAiModelTest().subscribe(status => {
      this.aiTestStarting.set(false);
      if (!status) {
        return;
      }
      this.aiTest.set({ ...status, state: 'running' });
      this.pollAiTest(IntegrationsStatusComponent.aiTestPollAttempts);
    });
  }

  aiVerdictLabel(verdict: AiModelTestVerdict): string {
    switch (verdict) {
      case 'passed': return 'integration.allTestedAIPlanningFeaturesAreAvailable';
      case 'warnings': return 'integration.aIPlanningWorksButSomeFeaturesAreUnavailable';
      case 'failed': return 'integration.aIPlanningIsUnavailableSuggestionsWillUseTheRulesFallback';
    }
  }

  /** Translate diagnostic test checks into stable, user-facing product features. */
  aiFeature(check: AiModelTestCheck): { name: string; detail: string } {
    const features: Record<string, { name: string; supported: string; unsupported: string }> = {
      Endpoint: {
        name: 'integration.aIServiceConnection',
        supported: 'integration.dishhiveCanConnectToTheConfiguredAIService',
        unsupported: 'integration.dishhiveCannotConnectToTheConfiguredAIService'
      },
      'Model available': {
        name: 'integration.configuredModel',
        supported: 'integration.theConfiguredModelIsAvailableForMealPlanning',
        unsupported: 'integration.theConfiguredModelIsNotAvailableFromTheAIService'
      },
      'Structured JSON reply': {
        name: 'integration.structuredMealPlans',
        supported: 'integration.theModelReturnsMealPlansInAFormatDishhiveCanUse',
        unsupported: 'integration.theModelDoesNotReturnMealPlansInAUsableFormat'
      },
      'All days filled': {
        name: 'integration.completeWeekPlanning',
        supported: 'integration.theModelCanProposeMealsForEveryRequestedDay',
        unsupported: 'integration.theModelMayLeaveRequestedDaysWithoutAMealSuggestion'
      },
      'Collection day': {
        name: 'integration.recipeCollectionInstructions',
        supported: 'integration.theModelCanChooseRecipesFromARequestedCollection',
        unsupported: 'integration.theModelDoesNotReliablyFollowRecipeCollectionRequests'
      },
      'Specific dish': {
        name: 'integration.specificMealInstructions',
        supported: 'integration.theModelCanPlaceARequestedMealOnTheRequestedDay',
        unsupported: 'integration.theModelDoesNotReliablyFollowMealAndDayInstructions'
      },
      'Vegetarian days': {
        name: 'integration.dietaryPlanningInstructions',
        supported: 'integration.theModelCanFollowDietaryPlanningInstructionsAcrossTheWeek',
        unsupported: 'integration.theModelDoesNotReliablyFollowDietaryPlanningInstructions'
      },
      'External recipe intent': {
        name: 'integration.externalRecipeDiscovery',
        supported: 'integration.theModelCanInterpretSourceCountDateAndCourseRequestsForVerifiedRecipeResearch',
        unsupported: 'integration.theModelCannotReliablyInterpretExternalRecipeRequests'
      },
      'Multilingual planning instructions': {
        name: 'integration.multilingualSourceRequests',
        supported: 'integration.theModelCanTurnEquivalentEnglishAndDutchSourceRequestsIntoUsableCountsDatesAndCourses',
        unsupported: 'integration.atLeastOneEnglishOrDutchSourceRequestDidNotProduceAUsableCountDateAndCoursePlanThisIsStricterThanUnderstandingConversationalLanguage'
      },
      'Test run': {
        name: 'Capability test',
        supported: 'The model capability test completed successfully.',
        unsupported: 'The model capability test could not be completed.'
      }
    };
    const feature = features[check.name];
    return feature
      ? { name: feature.name, detail: check.passed ? feature.supported : feature.unsupported }
      : {
          name: check.name,
          detail: check.passed ? 'This capability is supported.' : 'This capability is not supported.'
        };
  }

  private pollAiTest(attemptsLeft: number): void {
    setTimeout(() => {
      this.integrationsService.getAiModelTest().subscribe(test => {
        if (test && test.state !== 'running') {
          this.aiTest.set(test);
          // The status row carries the verdict chip context; refresh it too
          this.integrationsService.getStatus().subscribe(status => this.integrations.set(status));
        } else if (attemptsLeft > 1) {
          this.pollAiTest(attemptsLeft - 1);
        } else if (test) {
          this.aiTest.set(test); // still running after the poll window; leave as-is
        }
      });
    }, IntegrationsStatusComponent.updatePollIntervalMs);
  }

  chipClass(integration: { configured: boolean; reachable: boolean }): string {
    if (!integration.configured) return 'status-chip--off';
    return integration.reachable ? 'status-chip--ok' : 'status-chip--warn';
  }

  chipIcon(integration: { configured: boolean; reachable: boolean }): string {
    if (!integration.configured) return 'radio_button_unchecked';
    return integration.reachable ? 'check_circle' : 'warning';
  }

  chipLabel(integration: { configured: boolean; reachable: boolean }): string {
    if (!integration.configured) return 'common.notConfigured';
    return integration.reachable ? 'common.active' : 'common.unreachable';
  }

  webSearchChipClass(integration: IntegrationStatusResponse['webSearch']): string {
    if (!integration.configured) return 'status-chip--off';
    return integration.operational ? 'status-chip--ok' : 'status-chip--warn';
  }

  webSearchChipIcon(integration: IntegrationStatusResponse['webSearch']): string {
    if (!integration.configured) return 'radio_button_unchecked';
    return integration.operational ? 'check_circle' : 'warning';
  }

  webSearchChipLabel(integration: IntegrationStatusResponse['webSearch']): string {
    if (!integration.configured) return 'common.notConfigured';
    if (integration.operational) return 'common.active';
    return integration.reachable ? 'common.misconfigured' : 'common.unreachable';
  }

  checkForUpdates(): void {
    this.checkingVersion.set(true);
    this.scraperMessage.set(null);
    this.integrationsService.checkScraperVersion().subscribe(result => {
      this.checkingVersion.set(false);
      this.versionCheck.set(result);
      if (!result) {
        this.scraperMessage.set('Could not check for updates — is the scraper container running?');
      }
    });
  }

  installUpdate(): void {
    const target = this.versionCheck()?.latestVersion ?? undefined;
    this.updating.set(true);
    this.scraperMessage.set(null);
    this.integrationsService.updateScraper(target).subscribe(result => {
      if (!result) {
        this.updating.set(false);
        this.scraperMessage.set('Update failed — check the scraper container logs.');
        return;
      }
      // The sidecar restarts to load the new version; poll until it is back
      this.pollUntilScraperBack(result.targetVersion, IntegrationsStatusComponent.updatePollAttempts);
    });
  }

  private pollUntilScraperBack(targetVersion: string | null, attemptsLeft: number): void {
    setTimeout(() => {
      this.integrationsService.getStatus().subscribe(status => {
        const scraper = status?.scraper;
        const back = !!scraper?.reachable
          && (!targetVersion || scraper.packageVersion === targetVersion);

        if (back) {
          this.integrations.set(status);
          this.updating.set(false);
          this.versionCheck.set(null);
          this.scraperMessage.set(`Updated to v${scraper!.packageVersion}.`);
        } else if (attemptsLeft <= 1) {
          if (status) this.integrations.set(status);
          this.updating.set(false);
          this.scraperMessage.set('The scraper service has not come back up yet — refresh the page in a moment.');
        } else {
          this.pollUntilScraperBack(targetVersion, attemptsLeft - 1);
        }
      });
    }, IntegrationsStatusComponent.updatePollIntervalMs);
  }
}
