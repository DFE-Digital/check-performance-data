# Each base image is pinned by digest, which Dependabot keeps current (docs/dependabot.md#pins).
FROM nginx/nginx-unprivileged:1.30.7-alpine3.24@sha256:0c53a1f2890c3526bfd201440f0fd3970e5b78f80630f58284aad0d34b5ec7bd

COPY nginx.conf /etc/nginx/nginx.conf
COPY ./html/ /usr/share/nginx/html
