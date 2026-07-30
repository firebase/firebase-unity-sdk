// Copyright 2026 Google Inc. All rights reserved.
//
// Licensed under the Apache License, Version 2.0 (the "License");
// you may not use this file except in compliance with the License.
// You may obtain a copy of the License at
//
//     http://www.apache.org/licenses/LICENSE-2.0
//
// Unless required by applicable law or agreed to in writing, software
// distributed under the License is distributed on an "AS IS" BASIS,
// WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
// See the License for the specific language governing permissions and
// limitations under the License.

const { onCall, HttpsError } = require("firebase-functions/https");

// Adds two numbers to each other.
exports.addNumbers = onCall((request) => {
  // Numbers passed from the client.
  const firstNumber = request.data.firstNumber;
  const secondNumber = request.data.secondNumber;

  // Checking that attributes are present and are numbers.
  if (!Number.isFinite(firstNumber) || !Number.isFinite(secondNumber)) {
    // Throwing an HttpsError so that the client gets the error details.
    throw new HttpsError('invalid-argument', 'The function ' +
        'must be called with two arguments "firstNumber" and "secondNumber" ' +
        'which must both be numbers.');
  }

  // returning result.
  return {
    firstNumber: firstNumber,
    secondNumber: secondNumber,
    operator: '+',
    operationResult: firstNumber + secondNumber,
  };
});

// Creates a function that consumes limited-use App Check tokens
exports.addtwowithlimiteduse = onCall({
  enforceAppCheck: true,
  consumeAppCheckToken: true,
  maxInstances: 10
}, (request) => {
  // request.app will be defined if a valid App Check token was provided
  if (request.app === undefined) {
    throw new HttpsError(
        'failed-precondition',
        'The function must be called from an App Check verified app.');
  }

  const firstNumber = request.data.firstNumber;
  const secondNumber = request.data.secondNumber;

  if (firstNumber === undefined || secondNumber === undefined) {
     throw new HttpsError('invalid-argument', 'The function must be called with "firstNumber" and "secondNumber".');
  }

  return {
    firstNumber: firstNumber,
    secondNumber: secondNumber,
    operator: '+',
    operationResult: Number(firstNumber) + Number(secondNumber),
  };
});

function sleep(ms) {
  return new Promise(resolve => setTimeout(resolve, ms));
};

const streamData = ["hello", "world", "this", "is", "cool"];

async function* generateText() {
  for (const chunk of streamData) {
    yield chunk;
    await sleep(100);
  }
};

/**
 * A streaming callable function that streams the elements of `streamData`
 * ("hello", "world", "this", "is", "cool") chunk by chunk when the client accepts
 * streaming, and returns the joined string "hello world this is cool" as the final response.
 */
exports.genStream = onCall(
  async (request, response) => {
    if (request.acceptsStreaming) {
      for await (const chunk of generateText()) {
        response.sendChunk(chunk);
      }
    }
    return streamData.join(" ");
  }
);

exports.genStreamError = onCall(
  async (request, response) => {
    throw Error("BOOM");
  }
);

