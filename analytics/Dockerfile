FROM python:3.13-slim
WORKDIR /app
ENV DYNOLAB_ROOT=/app
COPY analytics/pyproject.toml analytics/
COPY analytics/src analytics/src
RUN pip install --no-cache-dir ./analytics
COPY config ./config
RUN useradd -m app
USER app
EXPOSE 8100
CMD ["dynolab", "serve", "--port", "8100"]
