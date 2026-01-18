/*
  ArduPong - sketch semplice e stabile per potenziometri
  - legge A0 (opzionale A1)
  - applica moving average per ridurre jitter
  - invia valori su Serial in formato: "512" oppure "512,600"
  - Baud: 9600
*/

const int POT1_PIN = A0;
const int POT2_PIN = A1;
const bool USE_POT2 = false; // true se colleghi un secondo pot a A1

const unsigned long SEND_INTERVAL_MS = 80; // invia ogni 80 ms (modificabile)
const int AVG_WINDOW = 8; // dimensione finestra moving average (più grande -> più liscio)

int buffer1[AVG_WINDOW];
int idx1 = 0;
long sum1 = 0;

int buffer2[AVG_WINDOW];
int idx2 = 0;
long sum2 = 0;

unsigned long lastSend = 0;

void setup() {
  Serial.begin(9600);
  // Inizializza buffer con la prima lettura per evitare transitori
  int v = analogRead(POT1_PIN);
  for (int i = 0; i < AVG_WINDOW; i++) buffer1[i] = v;
  sum1 = (long)v * AVG_WINDOW;

  if (USE_POT2) {
    int v2 = analogRead(POT2_PIN);
    for (int i = 0; i < AVG_WINDOW; i++) buffer2[i] = v2;
    sum2 = (long)v2 * AVG_WINDOW;
  }
}

int readSmoothed(int pin, int *buffer, int &idx, long &sum) {
  int raw = analogRead(pin);
  // Aggiorna somma / buffer circolare
  sum -= buffer[idx];
  buffer[idx] = raw;
  sum += raw;
  idx++;
  if (idx >= AVG_WINDOW) idx = 0;
  int avg = (int)(sum / AVG_WINDOW);
  return avg;
}

void loop() {
  unsigned long now = millis();
  if (now - lastSend >= SEND_INTERVAL_MS) {
    lastSend = now;

    int val1 = readSmoothed(POT1_PIN, buffer1, idx1, sum1);

    if (USE_POT2) {
      int val2 = readSmoothed(POT2_PIN, buffer2, idx2, sum2);
      // invia "val1,val2"
      Serial.print(val1);
      Serial.print(',');
      Serial.println(val2);
    } else {
      // invia "val1"
      Serial.println(val1);
    }
  }

  // puoi aggiungere altre logiche senza bloccare loop()
}