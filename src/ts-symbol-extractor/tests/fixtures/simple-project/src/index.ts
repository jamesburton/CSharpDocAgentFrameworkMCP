/**
 * This is a sample function.
 * @param name The name to greet.
 * @returns A greeting string.
 */
export function hello(name: string): string {
  return `Hello, ${name}!`;
}

export interface IGreeter {
  /**
   * Greets someone.
   */
  greet(name: string): string;
}

/**
 * A class that greets people.
 */
export class Greeter implements IGreeter {
  /**
   * @inheritdoc
   */
  greet(name: string): string {
    return hello(name);
  }
}

/**
 * A specialized greeter.
 */
export class SpecialGreeter extends Greeter {
  /**
   * Greets someone specially.
   */
  greetSpecially(name: string): string {
    return `Special ${this.greet(name)}`;
  }
}

/**
 * Cardinal directions.
 */
export enum Direction {
  North = 0,
  South = 1,
  East = 2,
  West = 3,
}

/**
 * A greeting string type alias.
 */
export type Greeting = string;

/**
 * A class with a constructor and field.
 */
export class ConfiguredGreeter extends Greeter {
  /** The greeting prefix. */
  public prefix: string;

  /**
   * Creates a new ConfiguredGreeter.
   * @param prefix The prefix to use.
   */
  constructor(prefix: string) {
    super();
    this.prefix = prefix;
  }

  greet(name: string): string {
    return `${this.prefix} ${super.greet(name)}`;
  }
}
