﻿module FsUnit

open NUnit.Framework
open NUnit.Framework.Constraints

let should (f : 'a -> #Constraint) x (y : obj) =
    let c = f x
    let y =
        match y with
        | :? (unit -> unit) -> box (new TestDelegate(y :?> unit -> unit))
        | _                 -> y
    Assert.That(y, c)

let equal x = new EqualConstraint(x)

// like "should equal", but validates same-type
let shouldEqual (x: 'a) (y: 'a) = Assert.AreEqual(x, y, sprintf "Expected: %A\nActual: %A" x y)


// like "should equal", but validates same-type
let shouldNotEqual (x: 'a) (y: 'a) = Assert.AreNotEqual(x, y, sprintf "Expected: %A\nActual: %A" x y)

// overriding a standard language function is not cool
//let not x = new NotConstraint(x)

let contain x = new ContainsConstraint(x)

let haveLength n = Has.Length.EqualTo(n)

let haveCount n = Has.Count.EqualTo(n)

let be = id

let Null = new NullConstraint()

let Empty = new EmptyConstraint()

let EmptyString = new EmptyStringConstraint()

let NullOrEmptyString = NUnit.Framework.Constraints.OrConstraint(Null, EmptyString)

let True = new TrueConstraint()

let False = new FalseConstraint()

let sameAs x = new SameAsConstraint(x)

let throw = Throws.TypeOf
