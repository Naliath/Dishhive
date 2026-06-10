export interface DishStatistic {
  dishName: string;
  timesPlanned: number;
  /** ISO date (yyyy-MM-dd) */
  lastPlanned: string;
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
  topDishes: DishStatistic[];
}
