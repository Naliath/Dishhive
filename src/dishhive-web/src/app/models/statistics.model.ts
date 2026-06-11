export interface DishStatistic {
  dishName: string;
  timesPlanned: number;
  /** ISO date (yyyy-MM-dd) */
  lastPlanned: string;
  /** How often this dish was marked as actually eaten */
  timesEaten: number;
  /** Average member rating (1-5); null when never rated */
  averageRating: number | null;
  /** Number of ratings of 4 or 5 ("loved") */
  lovedCount: number;
}

export interface DishStatistics {
  dishes: DishStatistic[];
  /** Vague-instruction-only meals, counted separately */
  unspecifiedCount: number;
}

export interface MemberStatistics {
  memberId: string;
  name: string;
  mealsAttended: number;
  /** Attended meals that were marked eaten */
  mealsEaten: number;
  /** Average rating this member gave; null when none */
  averageRatingGiven: number | null;
  topDishes: DishStatistic[];
}
