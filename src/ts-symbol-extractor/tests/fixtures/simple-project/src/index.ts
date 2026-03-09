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
