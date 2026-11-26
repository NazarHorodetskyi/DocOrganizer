// Навантажувальне тестування Documents Organizer API.
//
// 1. Отримай access token користувача petro (у нього 5000+ документів) у Postman:
//    папка «5. Чужі ресурси (petro)» → Authorization → Get New Access Token → скопіювати Access Token.
// 2. Запуск (PowerShell, API має працювати):
//    k6 run -e TOKEN=<access_token> --summary-export=loadtests/results-before.json loadtests/documents-load.js
//    k6 run -e TOKEN=<access_token> --summary-export=loadtests/results-after.json  loadtests/documents-load.js
//
// Токен Auth0 для API за замовчуванням живе 24 год (APIs → doc-organizer-api → Token Expiration),
// тож одного токена вистачає на обидва прогони.

import http from 'k6/http';
import { check, sleep, group } from 'k6';

const BASE = __ENV.BASE_URL || 'https://localhost:7042';
const TOKEN = __ENV.TOKEN;

export const options = {
  insecureSkipTLSVerify: true,           // dev-сертифікат ASP.NET Core
  stages: [
    { duration: '30s', target: 20 },     // розігрів
    { duration: '1m', target: 50 },      // основне навантаження
    { duration: '30s', target: 0 },      // спад
  ],
  thresholds: {
    http_req_duration: ['p(95)<300'],    // 95 % запитів швидше за 300 мс
    http_req_failed: ['rate<0.01'],      // помилок менше 1 %
  },
  summaryTrendStats: ['avg', 'min', 'med', 'p(90)', 'p(95)', 'p(99)', 'max'],
};

export function setup() {
  if (!TOKEN) throw new Error('Передай токен: k6 run -e TOKEN=<access_token> loadtests/documents-load.js');
  const me = http.get(`${BASE}/api/v1/me`, { headers: { Authorization: `Bearer ${TOKEN}` } });
  if (me.status !== 200) throw new Error(`Токен не приймається API: ${me.status} ${me.body}`);
  return { token: TOKEN };
}

const states = ['Valid', 'ExpiringSoon', 'Expired', 'Perpetual'];
const sorts = ['expiryDate', 'title', 'issueDate'];

export default function (data) {
  const params = { headers: { Authorization: `Bearer ${data.token}`, Accept: 'application/json' } };

  group('Список документів (фільтр + сортування + пагінація)', () => {
    const page = 1 + Math.floor(Math.random() * 5);
    const state = states[Math.floor(Math.random() * states.length)];
    const sort = sorts[Math.floor(Math.random() * sorts.length)];
    const res = http.get(
      `${BASE}/api/v1/documents?page=${page}&pageSize=20&expiryState=${state}&sortBy=${sort}`,
      Object.assign({ tags: { name: 'GET /documents' } }, params));
    check(res, {
      'documents: статус 200': (r) => r.status === 200,
      'documents: є метадані сторінки': (r) => r.json('totalCount') !== undefined,
    });
  });

  group('Календар завершення строків', () => {
    const res = http.get(`${BASE}/api/v1/documents/expiry-calendar?pageSize=50`,
      Object.assign({ tags: { name: 'GET /documents/expiry-calendar' } }, params));
    check(res, { 'calendar: статус 200': (r) => r.status === 200 });
  });

  group('Довідник типів', () => {
    const res = http.get(`${BASE}/api/v1/document-types`,
      Object.assign({ tags: { name: 'GET /document-types' } }, params));
    check(res, { 'types: статус 200': (r) => r.status === 200 });
  });

  sleep(1);
}
