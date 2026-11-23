# Each base image is pinned by digest, which Dependabot keeps current (docs/dependabot.md#pins).
FROM nginx/nginx-unprivileged:1.31.8-alpine3.24@sha256:f90ef283a8a580912aa56315c3ed8f54cf50c2e89ed7e6fbf4c53368b55d712f

COPY nginx.conf /etc/nginx/nginx.conf
COPY ./html/ /usr/share/nginx/html
