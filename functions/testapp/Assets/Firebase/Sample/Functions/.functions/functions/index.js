/**
 * Copyright 2018 Google Inc. All Rights Reserved.
 *
 * Licensed under the Apache License, Version 2.0 (the "License");
 * you may not use this file except in compliance with the License.
 * You may obtain a copy of the License at
 *
 *      http://www.apache.org/licenses/LICENSE-2.0
 *
 * Unless required by applicable law or agreed to in writing, software
 * distributed under the License is distributed on an "AS IS" BASIS,
 * WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
 * See the License for the specific language governing permissions and
 * limitations under the License.
 */
'use strict';

const functions = require('firebase-functions/v1');
const functionsV2 = require('firebase-functions/v2');
const admin = require('firebase-admin');
admin.initializeApp();

// Adds two numbers to each other.
exports.addNumbers = functions.https.onCall((data) => {
  // Numbers passed from the client.
  const firstNumber = data.firstNumber;
  const secondNumber = data.secondNumber;

  // Checking that attributes are present and are numbers.
  if (!Number.isFinite(firstNumber) || !Number.isFinite(secondNumber)) {
    // Throwing an HttpsError so that the client gets the error details.
    throw new functions.https.HttpsError('invalid-argument', 'The function ' +
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

exports.genStream = functionsV2.https.onCall(
  async (request, response) => {
    if (request.acceptsStreaming) {
      for await (const chunk of generateText()) {
        response.sendChunk(chunk);
      }
    }
    return streamData.join(" ");
  }
);

exports.genStreamError = functionsV2.https.onCall(
  async (request, response) => {
    throw Error("BOOM");
  }
);

